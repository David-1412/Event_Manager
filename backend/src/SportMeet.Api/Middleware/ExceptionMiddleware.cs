using Microsoft.AspNetCore.Mvc;
using SportMeet.Application.Common;
using SportMeet.Api.Common;

namespace SportMeet.Api.Middleware;

/// <summary>
/// The single place application exceptions become HTTP. Doing it here rather than
/// in each action means a controller cannot forget a case and leak a 500 with a
/// stack trace, and the frontend's ProblemDetails detection gets a consistent body.
/// Registered before endpoint routing so it also wraps model-binding failures.
/// </summary>
public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (NotFoundException ex)
        {
            // Expected for a stale bookmark or a deleted event, so not logged as an
            // error - that would make ordinary browsing look like incidents.
            _logger.LogInformation("Not found: {Message}", ex.Message);
            await WriteAsync(context, StatusCodes.Status404NotFound, ProblemDetailsDefaults.NotFoundFor(ex.Id));
        }
        catch (DomainRuleException ex)
        {
            _logger.LogInformation("Domain rule rejected the request: {Message}", ex.Message);
            await WriteAsync(context, StatusCodes.Status422UnprocessableEntity, ProblemDetailsDefaults.DomainRule(ex.Message));
        }
        catch (EventFullException ex)
        {
            // 409 + code "EventFull" is the contract JoinButton branches on for
            // "This event just filled up" (spec §7); 422 would read as a form
            // error and hide the rollback message.
            _logger.LogInformation("Join rejected, event {EventId} is full.", ex.EventId);
            await WriteAsync(context, StatusCodes.Status409Conflict, ProblemDetailsDefaults.EventFullConflict());
        }
        catch (Exception ex)
        {
            // Anything else is a bug. Logged with the path, and deliberately not
            // echoed to the client.
            _logger.LogError(ex, "Unhandled exception while serving {Path}.", context.Request.Path);
            await WriteAsync(context, StatusCodes.Status500InternalServerError, ProblemDetailsDefaults.Unhandled());
        }
    }

    private static async Task WriteAsync(HttpContext context, int status, ProblemDetails problem)
    {
        // The response may already have started if an action streamed before
        // throwing; in that case the only honest move is to abort the connection
        // rather than append a second body.
        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        // application/problem+json, not application/json: RFC 9457 names the media
        // type, and the frontend's api.ts only reads `code`/`errors` after parsing,
        // so an honest content type costs nothing and keeps generic clients working.
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
