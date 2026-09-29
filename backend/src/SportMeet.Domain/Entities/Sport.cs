using SportMeet.Domain.Enums;

namespace SportMeet.Domain.Entities;

public class Sport
{
    public int Id { get; set; }

    /// <summary>Display name, e.g. "Badminton".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Lowercase key that crosses the API boundary as EventListItemDto.Sport.
    /// Must stay in lockstep with the SportKey union in
    /// frontend/src/types/events.ts and the z.enum in create-event-schema.ts —
    /// it is a wire value, so renaming one is a breaking change for the client.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Emoji glyph. spec §12 keeps icons in the DB seed rather than
    /// hardcoded in components; the frontend only falls back to its own map.</summary>
    public string? Icon { get; set; }

    public ICollection<Event> Events { get; set; } = new List<Event>();
}
