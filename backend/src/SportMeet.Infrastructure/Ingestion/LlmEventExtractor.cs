using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportMeet.Application.Ingestion;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Ingestion;

/// <summary>
/// LLM-backed extraction over the OpenAI chat-completions endpoint, using structured
/// outputs so the response is schema-constrained rather than merely requested.
///
/// Chosen over the <c>OpenAI</c> NuGet package because nothing here needs what it wraps:
/// one POST to a documented shape, and that package carries its own source generator that
/// collides with <c>[GeneratedRegex]</c> in this assembly under <c>net8.0</c>. Fewer
/// moving parts, and the payload stays legible in an HTTP trace while a prompt is being
/// debugged — which is the trace that actually matters for this feature.
///
/// <para><b>Four outcomes, and the distinction between them is the whole design:</b></para>
/// <list type="bullet">
///   <item><description><b>A proposal.</b> The model answered and the answer parsed.</description></item>
///   <item><description><b><c>null</c> from the model</b> (<c>isEvent=false</c>). A real
///   answer meaning "not an invitation" — a correct result, not a failure, and it must not
///   fall back: running the heuristic over a newsletter because the model correctly
///   declined it would manufacture a draft out of an invoice.</description></item>
///   <item><description><b>Heuristic fallback.</b> Transport failure, non-2xx, unparsable
///   JSON, or no key configured. The heuristic's conservatism — it reports what it matched
///   and flags the rest missing — makes it a reasonable floor rather than a rival guess.</description></item>
///   <item><description><b>A throw</b>, only when the fallback itself throws, which the
///   caller turns into <c>ExtractionStatus.Error</c>.</description></item>
/// </list>
///
/// <para><b>Not retried.</b> One attempt per message per cycle. The poller runs every few
/// minutes and never re-reads a message it has already classified, so a retry would
/// multiply spend inside one cycle over a message that was probably malformed anyway; a
/// transient outage instead surfaces as fallbacks in the logs, which is visible and free.
/// <see cref="OpenAiOptions.MaxTokensPerCall"/> bounds one call, the caller bounds calls
/// per cycle.</para>
///
/// <para><b>Trust.</b> Nothing returned here is treated as authoritative downstream —
/// every draft passes a human. That is what makes feeding this untrusted mailbox text
/// acceptable. The prompt reduces how often a bad value needs catching; the review queue is
/// where it gets caught.</para>
///
/// <para><b>Sender reaches the model but not the fallback.</b> Widening
/// <see cref="IEventExtractor"/> rather than overloading here means the composition root
/// registers one service and the evaluator calls one method, so a caller can never pick the
/// path that skips the fallback by accident. The heuristic ignores both extra arguments and
/// resolves relative dates from the arrival time it is given.</para>
/// </summary>
public sealed class LlmEventExtractor(
    IHttpClientFactory httpFactory,
    IOptions<OpenAiOptions> options,
    IOptions<Application.Ingestion.IngestionOptions> ingestionOptions,
    HeuristicEventExtractor fallback,
    ILogger<LlmEventExtractor> logger) : IEventExtractor
{
    private const string HttpTemplateName = "openai";

    private readonly OpenAiOptions _options = options.Value;
    private readonly string _defaultTimezone = ingestionOptions.Value.DefaultTimezone;

    public string PromptVersion => _options.PromptVersion;

    /// <summary>Reported even when unconfigured, because <c>GET /api/ingestion/status</c>
    /// is how an operator discovers the extractor is not the one they think is running.</summary>
    public string Model => _options.Model;

    /// <inheritdoc />
    public async Task<ExtractedEvent?> ExtractAsync(
        string subject, string? sender, string bodyText, DateTimeOffset receivedAt,
        CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            // Warned on every call rather than logged once at startup: the extractor is
            // reached from the manual endpoint as well as the poller, so it must not
            // depend on either being alive to report the problem. An unset key in a
            // deployed container otherwise looks exactly like a deliberate
            // heuristic-only configuration.
            logger.LogWarning(
                "OpenAI:ApiKey is not configured; using {Fallback} for this message. " +
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
                "LLM extraction ok: confidence={Confidence} missingCount={MissingCount}",
                proposal.Confidence, proposal.MissingFields.Count);
            return proposal;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown, not a bad response. Falling back would spend a heuristic run on a
            // message nobody is about to look at, and would swallow the cancellation.
            throw;
        }
        catch (Exception ex)
        {
            // Every failure shape — HTTP error, timeout, malformed JSON, schema
            // violation — degrades to the floor. A draft with four missing fields still
            // reaches a reviewer; a stalled pipeline silently loses other people's events.
            logger.LogWarning(ex,
                "LLM extraction failed, falling back to {Fallback}. raw={Raw}",
                fallback.Model, raw is null ? "(no response)" : Truncate(raw, 400));

            var degraded = await fallback
                .ExtractAsync(subject, sender, bodyText, receivedAt, ct)
                .ConfigureAwait(false);
            if (degraded is null) return null;

            // Nothing about the fallback's provenance is rewritten: model and promptVersion
            // must keep saying "heuristic", because a row that claims gpt-4o-mini while
            // holding regex output makes every later acceptance-rate comparison per model
            // meaningless. RawExtraction is the single addition — the text that failed, so
            // prompt iteration has something to look at beyond the exception message.
            return degraded.WithRawExtraction(Truncate(raw ?? ex.Message, 4000));
        }
    }

    /// <summary>One chat completion, returning the assistant message's text verbatim.
    ///
    /// The request body is built as a DOM rather than by serializing a record because
    /// <c>response_format.json_schema.schema</c> must be a raw JSON <em>object</em>, not a
    /// string containing JSON — a DTO property would need a <c>JsonNode</c> or a converter
    /// anyway, and the DOM keeps the payload shape obvious to whoever reads it next.</summary>
    private async Task<string> CompleteAsync(
        string subject, string? sender, string body, DateTimeOffset receivedAt, CancellationToken ct)
    {
        using var schema = JsonDocument.Parse(ExtractionPrompts.JsonSchema);

        var payload = new JsonObject
        {
            ["model"] = _options.Model,
            ["temperature"] = _options.Temperature,
            ["max_tokens"] = _options.MaxTokensPerCall,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = ExtractionPrompts.SystemPrompt(_options.PromptVersion),
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = ExtractionPrompts.UserPrompt(subject, sender, body, receivedAt),
                },
            },
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = JsonNode.Parse(schema.RootElement.GetRawText()),
            },
        };

        var client = httpFactory.CreateClient(HttpTemplateName);
        var url = $"{_options.BaseUrl.TrimEnd('/')}/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ApiKey}");

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Body included, because the status code alone does not separate the cases that
            // need different fixes: an invalid key, a spent quota and a model not enabled
            // on this endpoint all arrive as 401/403/404/429.
            throw new InvalidOperationException(
                $"Chat completion failed with {(int)response.StatusCode} {response.ReasonPhrase}: "
                + Truncate(text, 600));
        }

        return ExtractAssistantText(text);
    }

    /// <summary>Unwraps <c>choices[0].message.content</c>.
    ///
    /// Defensive about two shapes that occur in practice: content arrives as an <em>array
    /// of parts</em> from some reasoning models and gateways, and a gateway that ignores
    /// <c>response_format</c> returns the JSON wrapped in a code fence inside prose.
    /// Unwrapping both is worth the few lines — the alternative is discarding an extraction
    /// the model got right and falling back to the heuristic for it.</summary>
    private static string ExtractAssistantText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content");

        var raw = content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? "",
            JsonValueKind.Array => string.Concat(content.EnumerateArray()
                .Where(p => p.ValueKind == JsonValueKind.Object
                            && p.TryGetProperty("text", out var t)
                            && t.ValueKind == JsonValueKind.String)
                .Select(p => p.GetProperty("text").GetString())),
            _ => throw new InvalidOperationException(
                $"Unexpected content type in chat response: {content.ValueKind}"),
        };

        return StripCodeFence(raw).Trim();
    }

    private static string StripCodeFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return value;

        var firstBreak = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstBreak < 0 || lastFence <= firstBreak
            ? value
            : trimmed[(firstBreak + 1)..lastFence];
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}


