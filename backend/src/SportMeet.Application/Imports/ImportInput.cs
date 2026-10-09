using SportMeet.Domain.Enums;

namespace SportMeet.Application.Imports;

/// <summary>
/// What the user handed us. Everything downstream of extraction (geocoding, flagging,
/// recording, the response) sees only the <c>ExtractedEvent</c> that came out, never
/// this, so a new input is one new case in <see cref="ImportService"/>'s extraction
/// step and nothing else moves.
/// </summary>
public sealed record ImportInput(ImportInputKind Kind, string? Text);
