namespace SportMeet.Application.Ingestion;

/// <summary>
/// Turns an email body into an event proposal, or decides there isn't one.
///
/// Provider-agnostic on purpose: the plan starts with OpenAI but the whole point of
/// the seam is that swapping models is a DI change, not a rewrite. Implementations
/// live in Infrastructure; nothing in this layer may reference MailKit, MimeKit or
/// an HTTP client.
///
/// Deliberately *not* async-cancellable-with-retry here — retry and cost budgeting
/// belong to the caller, which knows how many messages are in the cycle.
/// </summary>
public interface IEventExtractor
{
    /// <summary>Current prompt/schema version, recorded on every row. Bump it
    /// whenever the prompt changes so acceptance rates stay comparable across
    /// versions and the golden set (§8) can be re-run per version.</summary>
    string PromptVersion { get; }

    /// <summary>Identifier of the underlying model, for cost attribution.</summary>
    string Model { get; }

    /// <summary>
    /// Returns the proposal, or <c>null</c> when the body is not an event
    /// invitation. A null result is a normal outcome and maps to
    /// <c>ExtractionStatus.NoEvent</c>, not to an error.
    /// </summary>
    /// <param name="subject">Header, passed separately because a subject often
    /// carries the title more reliably than the body does.</param>
    /// <param name="sender">Address the message arrived from, or null. An LLM uses it to
    /// tell a club newsletter from a player's own invite; a deterministic extractor can
    /// ignore it, and must not invent a title from it.</param>
    /// <param name="receivedAt">When the message arrived. A relative date ("next Friday",
    /// "tomorrow") has no answer without it, so an extractor that resolves relative dates
    /// needs this rather than its own clock: the poller reads up to a week of unread mail in
    /// one cycle, and a replay or a golden-set run must reproduce the original answer.</param>
    /// <param name="bodyText">Stripped plain text, already capped to
    /// <c>Ingestion:MaxBodyChars</c> by the caller.</param>
    Task<ExtractedEvent?> ExtractAsync(
        string subject, string? sender, string bodyText, DateTimeOffset receivedAt,
        CancellationToken ct = default);
}
