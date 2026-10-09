namespace SportMeet.Domain.Enums;

/// <summary>What the user handed us to build a draft from. Persisted as text, with no
/// CHECK listing the members, so a new kind is an enum value plus one case in
/// <c>ImportService</c> and no migration.</summary>
public enum ImportInputKind
{
    Text,
}

public enum ImportStatus
{
    /// <summary>The extractor proposed an event.</summary>
    Extracted,

    /// <summary>The input was not an event. Still recorded: it is a data point for the
    /// prompt, and the only way to count how often people paste the wrong thing.</summary>
    NoEvent,

    /// <summary>The user pasted a lone link, which is not supported. Recorded (host and
    /// path only) because it is the only measure of how much people want link import,
    /// and which sites they want it for.</summary>
    LinkRefused,
}
