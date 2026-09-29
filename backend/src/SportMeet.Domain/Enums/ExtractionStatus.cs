namespace SportMeet.Domain.Enums;

/// <summary>
/// Outcome of running the extractor over one ingested message. Text-mapped like
/// <see cref="EventStatus"/> so the values are readable straight from psql when
/// answering "why was my email ignored?".
///
/// <see cref="NoEvent"/> is a *successful* outcome, not a failure: the pre-filter
/// and the model both legitimately reject most mail, and recording that explicitly
/// is what keeps a silently-dropped invite distinguishable from a crashed poller.
/// </summary>
public enum ExtractionStatus
{
    Pending,
    Extracted,
    NoEvent,
    Error,
}
