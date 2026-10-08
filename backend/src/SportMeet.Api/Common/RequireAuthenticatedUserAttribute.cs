using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using SportMeet.Application.Common;

namespace SportMeet.Api.Common;

/// <summary>
/// Marks an endpoint as acting only on behalf of a *verified* caller: a request
/// whose identity did not come from a validated Firebase ID token is refused with
/// 401 before the action runs.
///
/// The distinction this enforces is the one <see cref="ICurrentUser.IsDemo"/>
/// reports: with no token at all, ICurrentUser falls back to the configured demo
/// identity — the deliberate development bypass that keeps browse/create usable
/// without Firebase credentials. That fallback is fine for anonymous browsing and
/// demo event creation, but a *private* resource (one user's draft queue) must
/// never resolve through it: with the demo id unset it would be a confusing 422,
/// and with it set every anonymous caller would share one queue. Both are worse
/// than an honest 401, which is also exactly what the frontend's auth guard
/// branches on.
///
/// The development bypass survives untouched: a request carrying a real Firebase
/// token, or one made while the demo id answers, only needs the demo restriction
/// lifted by configuring Firebase — which is the same switch everything else
/// uses.
///
/// <b>Stateless on purpose.</b> This attribute instance is shared by every request to
/// the action, so it must hold nothing about any one request. It used to be a
/// reusable filter factory that captured the scoped <c>ICurrentUser</c> of the first
/// request it served; that object caches its answer, so the first caller decided the
/// verdict for everyone after: an anonymous first call locked out signed-in users, and
/// an authenticated first call let anonymous callers through. The user is therefore
/// resolved from the current request's services, every time.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireAuthenticatedUserAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var currentUser = services.GetRequiredService<ICurrentUser>();
        if (!currentUser.IsDemo) return;

        services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<RequireAuthenticatedUserAttribute>()
            .LogWarning(
                "Private endpoint rejected at {Path}: hasBearer={HasBearer}, authenticated={Authenticated}, demoIdentity={DemoIdentity}",
                context.HttpContext.Request.Path,
                context.HttpContext.Request.Headers.Authorization.Count > 0,
                context.HttpContext.User.Identity?.IsAuthenticated == true,
                currentUser.IsDemo);

        // Demo identity is only a stand-in for "nobody verified is acting";
        // private per-user data must not be served to an unverified caller.
        // (IsDemo is false exactly when a verified Firebase token resolved,
        // and null when nobody resolved at all — both pass through; the
        // service itself turns "no identity" into a refusal.)
        context.Result = new ObjectResult(ProblemDetailsDefaults.Unauthorized())
        {
            StatusCode = StatusCodes.Status401Unauthorized,
        };
    }
}
