namespace SportMeet.Application.Ingestion;

/// <summary>
/// Settings for the ingestion pipeline. Declared in Application because the service
/// that consumes it lives here; it is *bound* in Infrastructure/Api from the
/// "Ingestion" configuration section, so no layer above this one leaks down.
///
/// Defaults are safe-to-run-empty: <see cref="Enabled"/> is false, so nothing polls
/// until someone opts in, and tests and CI get an inert pipeline without having to
/// configure anything.
/// </summary>
public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Master switch. Off by default: this feature reads someone's inbox and
    /// spends money per message, so it should never start because a deploy happened.</summary>
    public bool Enabled { get; init; }

    public int PollIntervalSeconds { get; init; } = 300;
    public int FetchBatchSize { get; init; } = 25;
    public int LookbackDays { get; init; } = 7;

    /// <summary>Hard cap on the text handed to the extractor. Bounds cost, and the
    /// injection surface, in one number (§5).</summary>
    public int MaxBodyChars { get; init; } = 12_000;

    /// <summary>Applied when an email states no zone. Melbourne because that is the
    /// product's market and the Event entity's own default.</summary>
    public string DefaultTimezone { get; init; } = "Australia/Melbourne";

    /// <summary>How long <c>body_text</c> is retained before it should be purged.
    /// The body is someone's personal correspondence, so it cannot live forever just
    /// because the row is convenient. Enforced by a scheduled sweep, not here.</summary>
    public int RetentionDays { get; init; } = 90;

    public IReadOnlyList<MailboxOptions> Mailboxes { get; init; } = [];

    /// <summary>Empty means "any sender". Documented in §5 as a *preference* filter,
    /// never a trust boundary: anyone who learns the address can mail the poller.</summary>
    public IReadOnlyList<string> AllowedSenders { get; init; } = [];

    /// <summary>Who the polled mailbox belongs to. The poller files every draft it
    /// creates under this user, which is what makes "drafts appear in my queue"
    /// true for automatic ingestion the same way it is for pasted text. Unset, the
    /// poller falls back to the configured demo identity and, failing that, a
    /// per-mailbox placeholder — a draft is never ownerless.</summary>
    public MailboxOwnerOptions? MailboxOwner { get; init; }
}

/// <summary>The mailbox-to-user mapping: the address being polled and the display
/// name to give the user row if it has to be created. The address is also the
/// match key (case-insensitive) against an existing users row's email.</summary>
public sealed class MailboxOwnerOptions
{
    public string Address { get; init; } = "";
    public string? Name { get; init; }
}

/// <summary>
/// Server-side verification of Firebase ID tokens. Declared in Application next to
/// <see cref="IngestionOptions"/> and bound in Infrastructure from the "Firebase"
/// section, so the implementation that talks to Google's JWKS endpoint stays out of
/// this layer.
///
/// The API accepts the browser's raw Firebase ID token as its bearer credential (no
/// exchanged API token). That only means anything if the token is *verified*, not
/// merely parsed: <see cref="IssuerPrefix"/> and <see cref="ProjectId"/> together pin
/// both the issuer and the audience, which is exactly the two claims an attacker
/// could otherwise forge by signing their own token with a different key.
///
/// Off unless a <see cref="ProjectId"/> is configured — the same "safe to run empty"
/// rule as <see cref="IngestionOptions.Enabled"/>. With no ProjectId the API keeps the
/// configured demo identity rather than half-enabling a broken auth path.
/// </summary>
public sealed class FirebaseOptions
{
    public const string SectionName = "Firebase";

    /// <summary>The Firebase project id, which is the token's <c>aud</c> claim. Empty
    /// means "do not verify tokens; use the configured demo identity".</summary>
    public string ProjectId { get; init; } = "";

    /// <summary>The <c>iss</c> prefix a valid token starts with. Firebase issues from
    /// securetoken.googleapis.com; overridable only so a test can point at a stub.</summary>
    public string IssuerPrefix { get; init; } = "https://securetoken.googleapis.com";

    /// <summary>True once a project is configured, i.e. verification is expected to
    /// run. Checked by <c>AddInfrastructure</c> to decide whether to install the
    /// bearer handler at all, so an unset ProjectId costs nothing at request time.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProjectId);

    /// <summary>The full expected issuer for a given project id — the JWT
    /// <c>iss</c> claim, validated exactly.</summary>
    public string ExpectedIssuer(string projectId) => $"{IssuerPrefix.TrimEnd('/')}/v2/{projectId}";
}


public sealed class MailboxOptions
{
    public string Name { get; init; } = "inbox";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 993;
    public string Username { get; init; } = "";

    /// <summary>Never committed. User secrets in Development, Key Vault in Azure.</summary>
    public string Password { get; init; } = "";

    public bool UseSsl { get; init; } = true;
}
