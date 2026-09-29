namespace SportMeet.Application.Ingestion;

/// <summary>
/// Azure AD app-registration settings for the Graph poller, bound from the
/// "AzureAd" section.
///
/// Declared in Application next to <see cref="IngestionOptions"/> and bound in
/// Infrastructure, for the same reason: the abstraction that consumes it
/// (<see cref="IEmailReader"/>) is an Application type, and the layer that
/// implements it must not be named here.
///
/// Secret handling follows the repo's existing rule for MailboxOptions.Password:
/// this property is the *slot*, never the committed value. User-secrets in
/// Development, environment variables or Key Vault elsewhere — see the note in
/// appsettings.json, which carries placeholders only.
/// </summary>
public sealed class GraphOptions
{
    public const string SectionName = "AzureAd";

    public string TenantId { get; init; } = "";
    public string ClientId { get; init; } = "";

    /// <summary>Client-secret *value*. Empty means unconfigured, which
    /// <see cref="GraphEmailReader"/> reports rather than discovering mid-poll.</summary>
    public string ClientSecret { get; init; } = "";

    /// <summary>The mailbox to poll, by address. App-only Graph tokens address mail
    /// as <c>/users/{address}/mailFolders/inbox</c>, so this is also the token
    /// subject — one value, not two.</summary>
    public string MailboxAddress { get; init; } = "";

    /// <summary>Graph's $top per request. Kept separate from
    /// <see cref="IngestionOptions.FetchBatchSize"/> because Graph caps a message
    /// collection at 999 and the two limits are set by different systems.</summary>
    public int FetchBatchSize { get; init; } = 50;

    /// <summary>Graph folder name. "Inbox" is the only value used today; configurable
    /// so the poller can be pointed at a subfolder during testing without a redeploy.</summary>
    public string Folder { get; init; } = "Inbox";

    /// <summary>True once every field Graph needs a token for is present. Checked by
    /// the hosted service *before* it contacts the network, so a misconfiguration is
    /// one clear startup log line instead of a repeating 401 in the poll loop.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(MailboxAddress);

    /// <summary>Which field is missing, for that startup message.</summary>
    public string DescribeMissing()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(TenantId)) missing.Add(nameof(TenantId));
        if (string.IsNullOrWhiteSpace(ClientId)) missing.Add(nameof(ClientId));
        if (string.IsNullOrWhiteSpace(ClientSecret)) missing.Add(nameof(ClientSecret));
        if (string.IsNullOrWhiteSpace(MailboxAddress)) missing.Add(nameof(MailboxAddress));
        return missing.Count == 0 ? "none" : string.Join(", ", missing);
    }
}
