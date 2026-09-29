using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SportMeet.Application.Events;

namespace SportMeet.Api.Common;

/// <summary>
/// Validation runs in the pipeline rather than inside each action: FluentValidation
/// executes before the action body, so EventService only ever receives a DTO that
/// already satisfies every bound, and the 422 shape is defined in one place.
///
/// 422 rather than the framework's default 400 is deliberate -
/// create-event-view.tsx branches on response.status === 422 to decide whether to
/// map field errors onto inputs, so a 400 would silently discard the server's
/// messages and show a generic failure.
/// </summary>
public sealed class ValidationFilter(IValidator<CreateEventDto> validator) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionArguments.TryGetValue("dto", out var raw) && raw is CreateEventDto dto)
        {
            var result = await validator.ValidateAsync(dto, context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
                    .ToDictionary(
                        g => g.Key,
                        g => g
                            .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Check this field" : e.ErrorMessage)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                        StringComparer.Ordinal);

                context.Result = new UnprocessableEntityObjectResult(ProblemDetailsDefaults.Validation(errors));
                return;
            }
        }

        await next();
    }
}
