using FluentValidation;
using SportMeet.Domain.Entities;

namespace SportMeet.Application.Events;

/// <summary>
/// Server-side copy of the client's rules in create-event-schema.ts.
///
/// Two rules the client cannot enforce for itself, and one deliberate
/// asymmetry:
///  - tags are normalized and length-checked here rather than trusted from the
///    client, because a tag is free text that becomes a durable row;
///  - MaxParticipants has no upper bound on the client (z.number().min(2)), so a
///    51 could be POSTed by a non-browser caller; the ceiling matches the
///    client's UI message ("50 is the maximum for now") and the DB CHECK.
///  - Description caps at the client's 1000 while the DB allows 2000: the
///    server is the ceiling, the client keeps its friendlier local message.
///
/// The old sport rule is gone with the sport vocabulary. SkillLevel is now
/// optional: the create form dropped its selector, so absence is the normal case
/// rather than an error.
/// </summary>
public sealed class CreateEventDtoValidator : AbstractValidator<CreateEventDto>
{

    public const int TitleMinLength = 3;
    public const int TitleMaxLength = 80;
    public const int VenueMaxLength = 120;
    public const int DescriptionMaxLength = 1000;
    public const int ParticipantsMin = 2;
    public const int ParticipantsMax = 50;
    public const decimal CostMax = 1000m;

    /// <summary>Tolerance for a form left open across a minute boundary: the
    /// create page validates "starts in the future" locally, and a submission a
    /// few seconds late must not fail for a reason the user cannot act on.</summary>
    public static readonly TimeSpan StartClockSkew = TimeSpan.FromMinutes(1);

    public CreateEventDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotNull().WithMessage("Event name is required")
            .NotEmpty()
            .MinimumLength(TitleMinLength).WithMessage($"Keep the name at least {TitleMinLength} characters")
            .MaximumLength(TitleMaxLength).WithMessage($"Keep the name under {TitleMaxLength} characters")
            .OverridePropertyName(nameof(CreateEventDto.Title));

        // Tags are optional, but a tag that arrives must survive normalization.
        // The count limit is checked against the raw list rather than the
        // normalized one so "you sent 9" is reported for 9 rather than for the 4
        // that happened to survive.
        RuleFor(x => x.Tags)
            .Must(t => t is null || t.Count <= TagNormalizer.MaxTagsPerEvent)
            .WithMessage($"Use at most {TagNormalizer.MaxTagsPerEvent} tags");

        RuleFor(x => x.Tags)
            .Must(tags => tags is null || TagNormalizer.HasOnlyValid(tags))
            .WithMessage($"Each tag needs {TagNormalizer.MinTagLength}-{TagNormalizer.MaxTagLength} letters or numbers")
            .When(x => x.Tags is not null && x.Tags.Count <= TagNormalizer.MaxTagsPerEvent);

        RuleFor(x => x.SkillLevel)
            .IsInEnum().WithMessage("Pick a skill level")
            .When(x => x.SkillLevel.HasValue);


        RuleFor(x => x.StartAt)
            .NotNull().WithMessage("Pick a date and time");

        RuleFor(x => x.EndAt)
            .NotNull().WithMessage("Pick a finish time")
            .Must((dto, end) => dto.StartAt is { } start && end > start)
            .WithMessage("Finish must be after the start");

        RuleFor(x => x.StartAt)
            .Must(start => start is null || start >= DateTimeOffset.UtcNow - StartClockSkew)
            .WithMessage("Pick a time in the future")
            .When(x => x.StartAt is not null && x.EndAt is not null);

        RuleFor(x => x.VenueName)
            .NotNull().WithMessage("Pick a venue")
            .NotEmpty()
            .MaximumLength(VenueMaxLength).WithMessage($"Keep the venue name under {VenueMaxLength} characters");

        RuleFor(x => x.Address)
            .NotNull().WithMessage("Venue address is required")
            .NotEmpty();

        RuleFor(x => x.Latitude)
            .NotNull().WithMessage("Venue location is required")
            .InclusiveBetween(-90d, 90d).WithMessage("Venue location is out of range");

        RuleFor(x => x.Longitude)
            .NotNull().WithMessage("Venue location is required")
            .InclusiveBetween(-180d, 180d).WithMessage("Venue location is out of range");

        RuleFor(x => x.MaxParticipants)
            .NotNull().WithMessage("How many people can join?")
            .GreaterThanOrEqualTo(ParticipantsMin).WithMessage($"At least {ParticipantsMin} people")
            .LessThanOrEqualTo(ParticipantsMax).WithMessage($"{ParticipantsMax} is the maximum for now");

        RuleFor(x => x.Cost)
            .GreaterThanOrEqualTo(0m).WithMessage("Cost cannot be negative")
            .LessThanOrEqualTo(CostMax).WithMessage($"Keep the cost under {CostMax:0} dollars")
            .When(x => x.Cost.HasValue);

        RuleFor(x => x.Description)
            .MaximumLength(DescriptionMaxLength).WithMessage($"Keep the description under {DescriptionMaxLength} characters");

        // The client sends its resolved IANA zone; anything unparseable would
        // become a row nobody can render a local time from.
        RuleFor(x => x.Timezone)
            .Must(tz => string.IsNullOrWhiteSpace(tz) || IsKnownTimeZone(tz))
            .WithMessage("Unknown timezone");
    }

    private static bool IsKnownTimeZone(string id)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
