using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportMeet.Application.Ingestion;

namespace SportMeet.Infrastructure.Ingestion;

/// <summary>
/// LLM-backed extraction over the Anthropic (Claude) Messages API. The second provider
/// behind <see cref="IEventExtractor"/>, used when <see cref="Application.Ingestion.OpenAiOptions"/>
/// is not configured - the composition root prefers OpenAI and falls through here before the
/// heuristic, so extraction keeps working on a Claude key alone.
///
/// Chosen over the Anthropic NuGet package for the same reason the OpenAI extractor skips
/// its SDK: one POST to a documented shape, no source generator colliding with
/// <c>[GeneratedRegex]</c> in this assembly under <c>net8.0</c>, and a payload that stays
/// legible in an HTTP trace while a prompt is tuned.
///
/// <para><b>Same contract, different wire format.</b> The four outcomes are exactly the ones
/// <see cref="LlmEventExtractor"/> documents - a proposal, a genuine <c>null</c>
/// (<c>isEvent=false</c>, which must not fall back), heuristic fallback on any transport or
/// parse failure, and a throw only when the fallback itself throws. What differs is only the
/// request/response shape: the Messages API takes the system prompt as a top-level
/// <c>system</c> field rather than a message, requires <c>max_tokens</c>, authenticates with
/// an <c>x-api-key</c> + <c>anthropic-version</c> header pair instead of a bearer token, and
/// returns text at <c>content[*].text</c>. It has no structured-output mode, so the prompt
/// carries an explicit JSON-only reminder and <see cref="StripCodeFence"/> handles the model
/// wrapping the object in a fence; the schema is still sent verbatim inside the system
/// message so the field list and constraints are identical across providers.</para>
///
/// <para><b>Not retried, and nothing downstream trusts the result</b> - both inherited
/// verbatim from <see cref="LlmEventExtractor"/>. Every draft passes a human.</para>
/// </summary>
public sealed class ClaudeEventExtractor(
    IHttpClientFactory httpFactory,
    IOptions<ClaudeOptions> options,
    IOptions<Application.Ingestion.IngestionOptions> ingestionOptions,
    HeuristicEventExtractor fallback,
    ILogger<ClaudeEventExtractor> logger) : IEventExtractor
{
    private const string HttpTemplateName = "claude";

    private readonly ClaudeOptions _options = options.Value;
    private readonly string _defaultTimezone = ingestionOptions.Value.DefaultTimezone;

    public string PromptVersion => _options.PromptVersion;

    /// <summary>Reported even when unconfigured, because <c>GET /api/ingestion/status</c>
    /// is how an operator discovers which extractor is actually running.</summary>
    public string Model => _options.Model;

    /// <inheritdoc />
    public async Task<ExtractedEvent?> ExtractAsync(
        string subject, string? sender, string bodyText, DateTimeOffset receivedAt,
        CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            // Reached only when the composition root selected Claude, so an unconfigured
            // key here means the key was cleared after startup. Warn rather than throw:
            // the heuristic is the floor for every provider.
            logger.LogWarning(
                "Claude:ApiKey is not configured; using {Fallback} for this message. " +
                "Set it in user-secrets (Development) or the environment (deployment).",
                fallback.Model);
            return await fallback
                .ExtractAsync(subject, sender, bodyText, receivedAt, ct)
                .ConfigureAwait(false);
        }

        string? raw = null;
        try
        {
            raw = await CompleteAsync(subject, sender, bodyText, receivedAt, ct).ConfigureAwait(false);

            var parsed = LlmExtractionResponse.Parse(raw);
            if (parsed is null)
            {
                logger.LogInformation(
                    "Model reports no event (confidence may be set); raw={Raw}", Truncate(raw, 400));
                return null;
            }

            var proposal = parsed.ToExtractedEvent(_options.PromptVersion, _options.Model, raw, _defaultTimezone);
            logger.LogDebug(
                "Claude extraction ok: confidence={Confidence} missingCount={MissingCount}",
                proposal.Confidence, proposal.MissingFields.Count);
            return proposal;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown, not a bad response - see LlmEventExtractor for why this rethrows.
            throw;
        }
        catch (Exception ex)
        {
            // Do not discard a response that yielded real fields for a formatting defect. The
            // raw text may still hold a usable object even though Parse threw (a repair the
            // salvage pass accepts, or a truncation whose leading members are intact). Only
            // fall back when nothing salvageable exists.
            if (raw is not null && TrySalvage(raw, ex, out var salvaged, out var salvageNote))
            {
                logger.LogWarning(
                    "Claude JSON was malformed but salvageable ({Note}); using the salvageable "
                    + "fields instead of falling back. raw={Raw}",
                    salvageNote, Truncate(raw, 1000));
                return salvaged!;
            }

            // Nothing salvageable: degrade to the floor. Provenance is not rewritten - model
            // and promptVersion keep saying "heuristic" so per-model acceptance stays honest.
            logger.LogWarning(ex,
                "Claude extraction failed, falling back to {Fallback}. raw={Raw}",
                fallback.Model, raw is null ? "(no response)" : Truncate(raw, 400));

            var degraded = await fallback
                .ExtractAsync(subject, sender, bodyText, receivedAt, ct)
                .ConfigureAwait(false);
            if (degraded is null) return null;

            return degraded.WithRawExtraction(Truncate(raw ?? ex.Message, 4000));
        }
    }

    /// <summary>
    /// Last-resort recovery from a response <see cref="LlmExtractionResponse.Parse"/> rejected.
    ///
    /// Repairs the raw text and, if that yields an object carrying a <c>title</c>, projects it
    /// with provenance marked as a salvage (the model and prompt version still name Claude, but
    /// <c>rawExtraction</c> records the defect and the exception so the reviewer sees the draft
    /// was rescued from a broken reply, not a clean extraction). Returns false when the raw text
    /// has no title - a response with no title has nothing a reviewer could act on, so the
    /// heuristic floor is genuinely the better answer there.
    /// </summary>
    private bool TrySalvage(string raw, Exception cause, out ExtractedEvent? proposal, out string note)
    {
        proposal = null;
        note = "";
        try
        {
            var repaired = LlmJson.Repair(raw);
            if (repaired is null) return false;

            var parsed = System.Text.Json.JsonSerializer
                .Deserialize<LlmExtractionResponse>(repaired);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Title)) return false;

            proposal = parsed.ToExtractedEvent(
                _options.PromptVersion, _options.Model,
                $"[salvaged: {cause.GetType().Name}: {Truncate(cause.Message, 500)}]\n{raw}",
                _defaultTimezone);
            note = cause.Message;
            return true;
        }
        catch (Exception)
        {
            // Salvage must never itself throw and mask the original failure path.
            return false;
        }
    }

    /// <summary>One Messages API call, returning the assistant text verbatim.
    ///
    /// DOM rather than a serialized record because the embedded schema and the system
    /// field are easier to assemble inline than to model, and the shape stays obvious to
    /// whoever reads the trace next.</summary>
    private async Task<string> CompleteAsync(
        string subject, string? sender, string body, DateTimeOffset receivedAt, CancellationToken ct)
    {
        // The Messages API has no strict structured-output mode, so the schema is passed
        // through the system prompt (identical field list to OpenAI) and reinforced with a
        // JSON-only reminder. `content[*].text` then carries the object.
        var system = ExtractionPrompts.SystemPrompt(_options.PromptVersion)
            + "\n\n" + ExtractionPrompts.JsonOnlyReminder();

        var payload = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = _options.MaxTokensPerCall,
            ["system"] = system,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = ExtractionPrompts.UserPrompt(subject, sender, body, receivedAt),
                },
            },
        };

        // Only send temperature when explicitly set positive: the 5.x Claude models reject
        // an explicit temperature outright (400), so the default path must not include it.
        if (_options.Temperature > 0)
            payload["temperature"] = _options.Temperature;

        var client = httpFactory.CreateClient(HttpTemplateName);
        var url = $"{_options.BaseUrl.TrimEnd('/')}/messages";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        // Anthropic authenticates with these two headers, not an Authorization bearer.
        request.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", _options.AnthropicVersion);

        // A key that is not scoped to a single workspace (a user key, or a key that can
        // reach several workspaces) is rejected with 400 unless the request names the
        // workspace. Only send it when configured: a workspace-scoped key must not receive
        // it, and an empty header value is itself a 400.
        if (!string.IsNullOrWhiteSpace(_options.WorkspaceId))
            request.Headers.TryAddWithoutValidation("anthropic-workspace-id", _options.WorkspaceId);


        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Body included: 401/403/404/429 do not separate an invalid key from a spent
            // quota or a model not enabled on this endpoint.
            throw new InvalidOperationException(
                $"Claude Messages call failed with {(int)response.StatusCode} {response.ReasonPhrase}: "
                + Truncate(text, 600));
        }

        return ExtractAssistantText(text);
    }

    /// <summary>Unwraps the text blocks of a Messages response. Content is an array of
    /// blocks; the <c>text</c> blocks are concatenated so a response split across blocks
    /// (or preceded by a thinking block) still yields the full object. A code fence is
    /// stripped because, without a schema-enforcing endpoint, the model occasionally wraps
    /// the JSON.</summary>
    private static string ExtractAssistantText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var content = doc.RootElement.GetProperty("content");

        if (content.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(
                $"Unexpected content shape in Messages response: {content.ValueKind}");

        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "text"
                && block.TryGetProperty("text", out var t)
                && t.ValueKind == JsonValueKind.String)
            {
                sb.Append(t.GetString());
            }
        }

        return LlmJson.StripCodeFence(sb.ToString()).Trim();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}

