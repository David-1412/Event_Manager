namespace SportMeet.Application.Imports;

/// <summary>Why a field is flagged. Strings, not an enum, because they cross the wire
/// as-is and the client maps each to a sentence.</summary>
public static class FlagReason
{
    /// <summary>Nothing was found. The user has to supply it.</summary>
    public const string Missing = "missing";

    /// <summary>Nothing was found and the form is showing a default instead.</summary>
    public const string Assumed = "assumed";

    /// <summary>Found, but the source did not state it clearly (a relative date, no
    /// time, a low-confidence title).</summary>
    public const string Unclear = "unclear";

    /// <summary>The date has already passed.</summary>
    public const string Past = "past";

    /// <summary>A place was named but the pin may not be right.</summary>
    public const string Unconfirmed = "unconfirmed";
}
