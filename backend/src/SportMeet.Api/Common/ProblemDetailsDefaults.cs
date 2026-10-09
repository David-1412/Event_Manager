using Microsoft.AspNetCore.Mvc;
using SportMeet.Application.Imports;

namespace SportMeet.Api.Common;

/// <summary>
/// The wire contract shared with frontend/src/lib/api.ts, which treats a body as
/// a ProblemDetails only when it carries a numeric `status` and a string `title`,
/// then branches on an optional `code` and an `errors` dictionary.
///
/// `code` is not part of RFC 9457 and ASP.NET Core never emits it, so every
/// ProblemDetails this app produces is built through here. The values match the
/// ProblemDetails["code"] union in the frontend's ApiError: ValidationError,
/// NotFound, EventFull. Only the first two occur in Milestone 1; EventFull arrives
/// with the participation endpoints.
/// </summary>
public static class ProblemDetailsDefaults
{
    /// <summary>Media type advertised in responses. The RFC 9457 type, so generic
    /// HTTP clients still recognise the shape.</summary>
    public const string Type = "https://tools.ietf.org/html/rfc9457";

    public const string ValidationError = "ValidationError";
    public const string NotFound = "NotFound";
    public const string EventFull = "EventFull";

    public static ProblemDetails NotFoundFor(Guid? id) => new()
    {
        Type = Type,
        Title = "Not Found",
        Status = StatusCodes.Status404NotFound,
        Detail = id is { } value
            ? $"No event with id '{value}' exists."
            : "The requested resource does not exist.",
        Extensions = { ["code"] = NotFound },
    };

    /// <summary>
    /// Field errors keyed by C# property name in PascalCase, e.g.
    /// "MaxParticipants". create-event-view.tsx lower-cases keys with its own
    /// toCamel() before matching form fields, and that helper accepts both
    /// "max_participants" and "MaxParticipants" - so PascalCase is what lands the
    /// server's message on the right input.
    ///
    /// errors is always attached and never empty: the create form only shows a
    /// field-level error when Object.keys(error.errors).length > 0, and otherwise
    /// falls through to a generic failure with no hint of what to fix.
    /// </summary>
    public static ProblemDetails Validation(IDictionary<string, string[]> errors)
    {
        var payload = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (field, messages) in errors)
        {
            payload[field] = messages is { Length: > 0 } ? messages : ["Check this field"];
        }

        return new ProblemDetails
        {
            Type = Type,
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status422UnprocessableEntity,
            Extensions =
            {
                ["code"] = ValidationError,
                ["errors"] = payload,
            },
        };
    }

    /// <summary>A violated business rule that has no single field to blame, e.g.
    /// an unknown sport slug. Still carries a non-empty errors entry so the create
    /// form can surface the message instead of a bare failure.</summary>
    public static ProblemDetails DomainRule(string message) => Validation(new Dictionary<string, string[]>
    {
        ["detail"] = [message],
    });

    /// <summary>Join lost the race for the last spot. The client's copy is
    /// JoinButton's own ("This event just filled up"), so no detail is needed -
    /// only the status and the code.</summary>
    public static ProblemDetails EventFullConflict() => new()
    {
        Type = Type,
        Title = "This event is full.",
        Status = StatusCodes.Status409Conflict,
        Extensions = { ["code"] = EventFull },
    };

    /// <summary>A private surface reached without a verified identity. The client
    /// treats 401 as "sign in", which is exactly the honest answer here: the draft
    /// queue is per-user, and an unverified caller has none.</summary>
    public static ProblemDetails Unauthorized() => new()
    {
        Type = Type,
        Title = "Authentication required.",
        Detail = "Sign in to view or manage your drafts.",
        Status = StatusCodes.Status401Unauthorized,
        Extensions = { ["code"] = "Unauthorized" },
    };

    /// <summary>A refusal from the import pipeline that the user can act on. The title is
    /// the sentence shown in the UI, so it is written for a person.</summary>
    public static ProblemDetails Import(ImportErrorCode code, string message)
    {
        var (status, name) = code switch
        {
            ImportErrorCode.EmptyInput => (StatusCodes.Status422UnprocessableEntity, "ImportEmptyInput"),
            ImportErrorCode.TooLong => (StatusCodes.Status422UnprocessableEntity, "ImportTooLong"),
            ImportErrorCode.UrlNotSupported => (StatusCodes.Status422UnprocessableEntity, "ImportUrlNotSupported"),
            ImportErrorCode.Busy => (StatusCodes.Status503ServiceUnavailable, "ImportBusy"),
            _ => (StatusCodes.Status422UnprocessableEntity, "Unknown"),
        };
        return new ProblemDetails
        {
            Type = Type,
            Title = message,
            Status = status,
            Detail = message,
            Extensions = { ["code"] = name },
        };
    }

    public static ProblemDetails RateLimited() => new()
    {
        Type = Type,
        Title = "Too many imports.",
        Status = StatusCodes.Status429TooManyRequests,
        Detail = "You've imported a lot in the last hour. Try again shortly, or fill the form in directly.",
        Extensions = { ["code"] = "ImportRateLimited" },
    };

    /// <summary>Catch-all. The client gets a stable shape and no internals; the
    /// exception itself is logged by the middleware with the trace id.</summary>
    public static ProblemDetails Unhandled() => new()
    {
        Type = Type,
        Title = "An unexpected error occurred.",
        Status = StatusCodes.Status500InternalServerError,
        Detail = "The request could not be completed. Please try again.",
        Extensions = { ["code"] = "Unknown" },
    };
}
