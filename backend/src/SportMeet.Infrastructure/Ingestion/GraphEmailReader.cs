using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportMeet.Application.Ingestion;

namespace SportMeet.Infrastructure.Ingestion;

/// <summary>
/// Reads the dedicated mailbox through Microsoft Graph, over client-credentials (app-only).
///
/// Graph rather than IMAP because it needs no mailbox password: an app registration with
/// the <c>Mail.ReadBasic</c> *application* permission reads one specified mailbox, and the
/// secret is revocable without touching the mailbox owner's credentials. It also removes
/// MailKit/MimeKit from the dependency tree, which matters while new packages must pass
/// through the corporate proxy.
///
/// App-only is the only workable grant here — a daemon has nobody to interactively sign in
/// as — and its consequence should be stated plainly: <c>Mail.ReadBasic</c> is tenant-wide
/// and cannot be scoped to a single mailbox at consent time. The narrowness comes from
/// <see cref="GraphOptions.MailboxAddress"/> being the only address this class ever
/// requests. That is a code-level constraint, and the reason the address is required
/// configuration rather than something inferred at runtime.
///
/// One <see cref="GraphServiceClient"/> for the container's lifetime: it holds an
/// HttpClient and a token cache, so building it per poll would re-authenticate every five
/// minutes for no reason.
/// </summary>
public sealed class GraphEmailReader : IEmailReader
{
    private readonly GraphOptions _graph;
    private readonly ILogger<GraphEmailReader> _logger;
    private readonly GraphServiceClient? _client;

    /// <summary>Populated on first successful fetch, so "is it working" is answerable
    /// without a log. Graph caps a message collection at 999; it caps at 100 for the
    /// messages *collection* path, which is the one used here.</summary>
    private int _requestedTop;

    public GraphEmailReader(IOptions<GraphOptions> graph, ILogger<GraphEmailReader> logger)
    {
        _graph = graph.Value;
        _logger = logger;

        if (!_graph.IsConfigured)
        {
            // Not a throw. Ingestion defaults to off, and a container with no Azure
            // credentials must still boot and serve the API; the hosted service checks
            // IsConfigured and stays asleep.
            _logger.LogWarning(
                "Ingestion: AzureAd section incomplete (missing {Missing}); Graph reader will return no mail",
                _graph.DescribeMissing());
            return;
        }

        var credential = new ClientSecretCredential(_graph.TenantId, _graph.ClientId, _graph.ClientSecret);

        // Mail.ReadBasic is the minimum scope that yields sender, subject and body and is
        // the lowest-blur application permission available for this. It is still
        // tenant-wide; see the class remark.
        _client = new GraphServiceClient(credential, new[] { "Mail.ReadBasic" });
        _requestedTop = Math.Clamp(_graph.FetchBatchSize, 1, 100);
    }

    public string Transport => "graph";

    public async Task<IReadOnlyList<FetchedEmail>> FetchUnreadAsync(CancellationToken ct = default)
    {
        if (_client is null) return [];

        // bodyPreview over body.content: one request returns the whole collection, and
        // fetching full bodies would mean a round trip per message for HTML mail. The
        // trade is truncation — Graph truncates bodyPreview to ~104 characters — so when
        // the preview is suspiciously short the message is refetched for its real body.
        // That keeps a well-formed invite readable while costing one extra call for the
        // truncated minority rather than one per message.
        var response = await _client.Users[_graph.MailboxAddress]
            .MailFolders[_graph.Folder]
            .Messages
            .GetAsync(q =>
            {
                // QueryParameters, not the configuration object itself — the OData
                // options live one level down in the Kiota-generated builders.
                q.QueryParameters.Filter = "isRead eq false";
                // List<string>, not a string: the Kiota builders model $orderby as
                // repeatable, so a plain string will not assign.
                // "Orderby" with a lowercase b — Kiota's generated naming, which is why
                // the OData convention does not compile here — and it is string[], so
                // multiple sort keys are expressible but a single one still needs an array.
                q.QueryParameters.Orderby = new[] { "receivedDateTime asc" };
                q.QueryParameters.Top = _requestedTop;
                q.QueryParameters.Select = new[]
                {
                    "id", "subject", "from", "receivedDateTime", "bodyPreview",
                    "body", "hasAttachments", "internetMessageId",
                };
                // The collection's `attachments` navigation is *not* populated by
                // GetAsync even when selected — Graph requires the navigation
                // expansion syntax for it, and content only arrives with
                // $expand=attachments($expand=content). Without this, hasAttachments
                // reads true and the list stays empty, so an .ics invite would be
                // invisible to the ICS-preferred extraction path.
                q.QueryParameters.Expand = new[] { "attachments($expand=content)" };
            }, ct);

        var items = response?.Value ?? [];
        var results = new List<FetchedEmail>(items.Count);

        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id)) continue;

            ct.ThrowIfCancellationRequested();

            var (body, isHtml) = await ResolveBodyAsync(item, ct);

            // A calendar invite is structured data sitting beside the prose, and
            // the pipeline prefers it: find the first text/calendar attachment's
            // content (base64 in the OData payload). Absent or undecodable is not
            // an error — extraction just falls through to the body, exactly as
            // before attachments were read at all.
            var ics = ExtractIcsContent(item);

            // Graph's own id, not internetMessageId: the requirement is "Graph MessageId
            // as the unique identifier". internetMessageId is worth having in the row for
            // cross-system correlation but it is author-supplied and can be absent or
            // forged, so it is not a dedupe key.
            results.Add(new FetchedEmail(
                MessageId: item.Id,
                Subject: item.Subject ?? string.Empty,
                FromAddress: item.From?.EmailAddress?.Address ?? string.Empty,
                FromName: item.From?.EmailAddress?.Name ?? string.Empty,
                ReceivedAt: item.ReceivedDateTime,
                BodyText: body,
                IsHtml: isHtml,
                AttachmentCount: item.HasAttachments == true
                    ? item.Attachments?.Count ?? 0
                    : 0,
                AttachmentNames: (item.Attachments ?? [])
                    .OfType<Attachment>()
                    .Select(a => a.Name ?? string.Empty)
                    .Where(n => n.Length > 0)
                    .ToArray(),
                IcsContent: ics));
        }

        return results;
    }

    /// <summary>The first text/calendar attachment as ICS text, or null. Content
    /// bounds mirror <see cref="Application.Ingestion.IngestionOptions.MaxBodyChars"/>
    /// order of magnitude — a VEVENT is small by construction, and a "calendar"
    /// attachment that is really megabytes is not a calendar.</summary>
    private string? ExtractIcsContent(Message item)
    {
        var attachments = item.Attachments;
        if (attachments is null || attachments.Count == 0) return null;

        foreach (var attachment in attachments.OfType<FileAttachment>())
        {
            var isCalendar = string.Equals(
                attachment.ContentType, "text/calendar", StringComparison.OrdinalIgnoreCase);
            var namedIcs = attachment.Name?.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) == true;
            if (!isCalendar && !namedIcs) continue;
            if (attachment.ContentBytes is not { Length: > 0 and < 512_000 }) continue;

            try
            {
                var text = System.Text.Encoding.UTF8.GetString(attachment.ContentBytes);
                return text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase) ? text : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex,
                    "Ingestion: could not decode calendar attachment {Name} on {MessageId}",
                    attachment.Name, item.Id);
                return null;
            }
        }

        return null;
    }


    /// <summary>Body as text, plus whether it arrived as HTML.
    ///
    /// Preference order is deliberate. Plain-text-first because it is the author's own
    /// words with no markup to strip and no layout tables shredding sentences into
    /// fragments — Graph does not generate a plain part from HTML, so this only wins when
    /// the sender actually provided one. HTML is the fallback and goes through
    /// <see cref="HtmlToText"/>.
    ///
    /// <paramref name="item"/>'s <c>bodyPreview</c> is used when present, and the full
    /// body is fetched only when the preview is too short to work with — see the note in
    /// the fetch about the 104-character truncation.</summary>
    private async Task<(string Text, bool IsHtml)> ResolveBodyAsync(Message item, CancellationToken ct)
    {
        var contentType = item.Body?.ContentType?.ToString();
        var isHtml = string.Equals(contentType, "html", StringComparison.OrdinalIgnoreCase);
        var raw = item.Body?.Content;

        // A Select of "body" does return content on the collection path for most mail, so
        // the common case needs no second call.
        if (string.IsNullOrWhiteSpace(raw))
        {
            var preview = string.IsNullOrWhiteSpace(item.BodyPreview)
                ? null
                : HtmlToText.Convert(item.BodyPreview);

            // Preview-only is enough for a short invite but not a long one, so refetch
            // when the preview looks clipped rather than guessing per message.
            if (LooksClipped(preview))
                raw = await FetchFullBodyAsync(item.Id!, ct);
            else
                raw = preview;
        }
        else if (isHtml)
        {
            raw = HtmlToText.Convert(raw);
        }

        return (raw ?? string.Empty, isHtml);
    }

    /// <summary>Graph truncates bodyPreview near 104 characters; text of that length is
    /// almost certainly cut mid-sentence, and a mid-sentence start is exactly where the
    /// date and venue heuristics fail.</summary>
    private static bool LooksClipped(string? text)
        => string.IsNullOrWhiteSpace(text) || text.Trim().Length is >= 90 and <= 120;

    private async Task<string?> FetchFullBodyAsync(string messageId, CancellationToken ct)
    {
        try
        {
            var full = await _client!.Users[_graph.MailboxAddress]
                .Messages[messageId]
                .GetAsync(q => q.QueryParameters.Select = new[] { "id", "body" }, ct);

            var content = full?.Body?.Content;
            if (string.IsNullOrWhiteSpace(content)) return null;

            var html = string.Equals(
                full!.Body!.ContentType?.ToString(), "html", StringComparison.OrdinalIgnoreCase);
            return html ? HtmlToText.Convert(content) : content;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Falling back to whatever the preview gave beats dropping the message: a
            // truncated body usually still yields a title and a date, and a draft a
            // reviewer can finish by hand is better than a silently ignored invitation.
            _logger.LogWarning(ex,
                "Ingestion: full-body fetch failed for {MessageId}; using bodyPreview", messageId);
            return null;
        }
    }
}
