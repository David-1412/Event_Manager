namespace SportMeet.Domain.Entities;

/// <summary>
/// A user-supplied label on an event (#tennis, #club). Replaces the fixed sport
/// vocabulary as the browse filter: the client no longer has a closed SportKey
/// union, so nothing here is a wire value that needs a C# or TypeScript change
/// when a new tag appears.
///
/// Names are stored normalized (lowercase, no leading '#', whitespace collapsed)
/// by TagNormalizer. Nothing dedupes near-misses like #tennis and #tennisis -
/// curation was explicitly ruled out, so the unique index guarantees only exact
/// identity, and popularity counts follow from that.
/// </summary>
public class Tag
{
    public int Id { get; set; }

    /// <summary>Normalized display form. Unique; the comparison is ordinal, so
    /// 'Tennis' and 'tennis' cannot both exist because they normalize to the
    /// same value before insert rather than because the database folds case.</summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<EventTag> EventTags { get; set; } = new List<EventTag>();
}
