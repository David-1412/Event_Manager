namespace SportMeet.Application.Ingestion;

/// <summary>
/// Settings for the Claude-backed extractor, bound from the "Claude" section.
///
/// The second LLM provider behind <see cref="IEventExtractor"/>. It exists so extraction
/// keeps working when an OpenAI key is absent: the composition root prefers OpenAI and
/// falls through to Claude only when <see cref="OpenAiOptions.IsConfigured"/> is false.
/// Same shape as <see cref="OpenAiOptions"/> on purpose — same fields, same secret
/// handling, so switching providers is a config change rather than a different mental
/// model.
///
/// Secret handling matches <see cref="GraphOptions.ClientSecret"/>: this is a slot, never
/// a committed value. Development reads it from user-secrets, deployment from the
/// environment or Key Vault — <c>ApiKey</c> in <c>appsettings.json</c> must stay empty.
/// </summary>
public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";

    /// <summary>Messages-API host. Overridable so a compatible gateway or proxy is a
    /// config change rather than a new <see cref="IEventExtractor"/> implementation.</summary>
    public string BaseUrl { get; init; } = "https://api.anthropic.com/v1";

    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "claude-sonnet-5-5";

    /// <summary>Anthropic's <c>max_tokens</c> is required (unlike OpenAI's optional
    /// <c>max_tokens</c>). The schema response is one flat object, but <c>description</c>
    /// (up to 900 chars) sits mid-object and pushes the tail (<c>confidence</c>,
    /// <c>missingFields</c>, <c>reasoning</c>) past a tight ceiling - which truncates the
    /// JSON. This bounds a runaway loop while leaving room for a full object.</summary>
    public int MaxTokensPerCall { get; init; } = 1500;

    /// <summary>Temperature, sent only when <em>positive</em>.
    ///
    /// Default 0 means "omit the field". Newer Claude models (the 5.x line) reject an
    /// explicit <c>temperature</c> with a 400 ("deprecated for this model"), so the safe
    /// default is not to send it and let the model use its own default. Set it positive to
    /// pin sampling on a model that still accepts it. Unlike OpenAI, 0 is therefore not the
    /// "send it" value here.</summary>
    public double Temperature { get; init; } = 0;

    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Versioned separately from the OpenAI prompt: the Claude request carries an
    /// explicit JSON-only reminder because the Messages API has no strict structured-output
    /// mode, so the two prompts are not byte-identical and their acceptance rates are not
    /// directly comparable. Bump with every prompt change.</summary>
    public string PromptVersion { get; init; } = "claude-1";

    /// <summary>Version header sent as <c>anthropic-version</c>. A dated snapshot rather
    /// than <c>"1"</c> so behaviour does not silently shift under a running deployment.</summary>
    public string AnthropicVersion { get; init; } = "2023-06-01";

    /// <summary>Optional workspace id, sent as the <c>anthropic-workspace-id</c> header.
    ///
    /// Anthropic rejects a key that is <em>not</em> scoped to a single workspace - a
    /// user-scoped <c>sk-usr-</c> key, or a standard <c>sk-ant-api</c> key that can reach
    /// several workspaces - with a 400 unless the request names the workspace to bill and
    /// authorize against. A workspace-scoped key needs nothing here; any other key needs the
    /// workspace's id (the Console URL reads
    /// <c>console.anthropic.com/settings/workspaces/&lt;id&gt;</c>, an id shaped like
    /// <c>wrkspc_01AbC...</c>). Empty means the header is omitted entirely, which is correct
    /// for a workspace-scoped key.</summary>
    public string WorkspaceId { get; init; } = "";


    /// <summary>True when a call could actually be made. Checked per extraction rather than
    /// at startup, and consulted by the composition root to decide whether Claude is the
    /// active extractor when OpenAI is unconfigured.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Model)
        && Uri.IsWellFormedUriString(BaseUrl, UriKind.Absolute);
}
