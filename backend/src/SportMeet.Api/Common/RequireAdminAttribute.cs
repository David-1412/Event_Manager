using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using SportMeet.Application.Common;

namespace SportMeet.Api.Common;

/// <summary>
/// Marks an endpoint as acting only on behalf of a <em>verified administrator</em>:
/// a request with no identity gets 401, and a request whose identity is not an
/// Admin gets 404 — before the action runs.
///
/// Two checks, because the two failures mean different things.
/// <see cref="RequireAuthenticatedUserAttribute"/> rejects the demo fallback: an
/// unverified caller must not reach private per-user data. This attribute adds the
/// privilege test on top, and it reads the role from the caller's database row via
/// <see cref="ICurrentUser.IsAdmin"/> rather than from a token claim, so a demotion
/// takes effect on the next request instead of when the current token expires.
///
/// <b>404 rather than 403 for a signed-in Member.</b> The resources behind this
/// attribute — the full user list, the audit trail, the review queue — are ones
/// whose <em>existence</em> is itself privileged. A 403 tells a Member "this is
/// real and you are not allowed", which is precisely the confirmation the
/// pending-review queue already declines to give. 404 is the same answer an unknown
/// id gets, so the surface is indistinguishable from absent.
///
/// <b>Stateless on purpose</b>, for the same reason as the attribute above: the
/// instance is shared across requests, so <see cref="ICurrentUser"/> is resolved
/// from the current request's services every time. Caching it here would let the
/// first caller decide the verdict for everyone after — the exact bug that
/// attribute was rewritten to fix.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireAdminAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var currentUser = services.GetRequiredService<ICurrentUser>();

        if (currentUser.UserId is null)
        {
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger<RequireAdminAttribute>()
                .LogWarning("Admin endpoint rejected with 401 at {Path}", context.HttpContext.Request.Path);

            context.Result = new ObjectResult(ProblemDetailsDefaults.Unauthorized())
            {
                StatusCode = StatusCodes.Status401Unauthorized,
            };
            return;
        }

        if (!currentUser.IsAdmin)
        {
            // Logged, not returned: the response must stay indistinguishable from
            // "no such resource", but an operator needs to be able to see that
            // someone is probing the admin surface.
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger<RequireAdminAttribute>()
                .LogWarning(
                    "Admin endpoint rejected at {Path}: caller {UserId} is not an Admin",
                    context.HttpContext.Request.Path,
                    currentUser.UserId);

            context.Result = new ObjectResult(ProblemDetailsDefaults.NotFoundFor(null))
            {
                StatusCode = StatusCodes.Status404NotFound,
            };
        }
    }
}
