using FluentValidation;

namespace SportMeet.Application.Ingestion;

/// <summary>
/// Guards the two inputs the review endpoints accept.
///
/// <see cref="ApproveDraftValidator"/> is intentionally thin: it does **not**
/// re-validate the event, because <c>ApproveDraftDto.Event</c> is a
/// <c>CreateEventDto</c> and <c>EventService.CreateAsync</c> runs the real rules
/// via <c>CreateEventDtoValidator</c>. Duplicating the title/venue/capacity bounds
/// here would put the same rule in two places and guarantee they drift; approve
/// therefore inherits create's validation exactly, including the 422 mapping.
///
/// What it *does* check is the draft's own metadata, which create knows nothing
/// about — chiefly that the JSON round-tripped into a usable payload at all.
/// </summary>
public sealed class ApproveDraftValidator : AbstractValidator<ApproveDraftDto>
{
    public ApproveDraftValidator()
    {
        RuleFor(x => x.Event).NotNull().WithMessage("An event payload is required");

        RuleFor(x => x.Note)
            .MaximumLength(500).WithMessage("Keep the note under 500 characters");
    }
}

/// <summary>
/// A rejection reason is optional (a reviewer often rejects on the merits alone),
/// but it must not become a place to paste a page of prose.
/// </summary>
public sealed class RejectDraftValidator : AbstractValidator<RejectDraftDto>
{
    public RejectDraftValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Keep the reason under 500 characters");
    }
}
