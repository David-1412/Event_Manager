namespace SportMeet.Domain.Enums;

/// <summary>
/// Arrival order matters: the numbers are never persisted (the column is text,
/// stored via HasConversion&lt;string&gt;), but the frontend's SkillLevel union
/// is exactly these three PascalCase strings and spec §12 says the client
/// lower-cases them only for CSS class lookup. Renaming a member is therefore a
/// breaking API change, not just a refactor.
/// </summary>
public enum SkillLevel
{
    Beginner,
    Intermediate,
    Advanced,
}
