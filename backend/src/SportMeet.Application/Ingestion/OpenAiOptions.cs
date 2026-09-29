namespace SportMeet.Application.Ingestion;

/// <summary>
/// Settings for the LLM-backed extractor, bound from the "OpenAi" section.
///
/// Secret handling matches <see cref="GraphOptions.ClientSecret"/>: this is a slot, never
/// a committed value. Development reads it from user-secrets, deployment from the
/// environment or Key Vault — <c>ApiKey</c> in <c>appsettings.json</c> must stay empty.
/// </summary>
public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAi";

    /// <summary>Chat-completions host. Overridable so a compatible gateway — Azure
    /// OpenAI, or a local model while developing without a key — is a config change
    /// rather than a new <see cref="IEventExtractor"/> implementation.</summary>
    public string BaseUrl { get; init; } = "https://api.openai.com/v1";

    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "gpt-4o-mini";

    /// <summary>Completion ceiling. The schema response is small — one flat object with
    /// a short reasoning sentence — so this bounds a runaway repetition loop rather than
    /// shaping a normal answer.</summary>
    public int MaxTokensPerCall { get; init; } = 700;

    /// <summary>Temperature 0 by default. The task is transcription into a fixed schema,
    /// not generation; anything above 0 makes a bad extraction non-reproducible, which
    /// defeats the golden set.</summary>
    public double Temperature { get; init; } = 0;

    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Prompt/schema revision, recorded on every row so acceptance rates stay
    /// comparable across prompt edits and the golden set can be re-run per version.
    /// Bump it with every prompt change.</summary>
    public string PromptVersion { get; init; } = "llm-1";

    /// <summary>True when a call could actually be made. Checked per extraction rather
    /// than at startup: unlike the poller, the extractor is reached from the manual
    /// endpoint too, so an unconfigured key must degrade to the heuristic rather than
    /// prevent the API from starting.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Model)
        && Uri.IsWellFormedUriString(BaseUrl, UriKind.Absolute);
}
