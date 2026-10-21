namespace SportMeet.Domain.Enums;

/// <summary>How often a recurring event repeats. Stored upper-case ("WEEKLY").</summary>
public enum RecurrenceFrequency
{
    Daily,
    Weekly,

    /// <summary>Every other week, on the chosen days ("fortnightly").</summary>
    Biweekly,

    /// <summary>The same day of the month as the first session.</summary>
    Monthly,
}
