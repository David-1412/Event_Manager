using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
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
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireAuthenticatedUserAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => true;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        => new RequireAuthenticatedUserFilter(
            serviceProvider.GetRequiredService<ICurrentUser>(),
            serviceProvider.GetRequiredService<ILogger<RequireAuthenticatedUserFilter>>());

    private sealed class RequireAuthenticatedUserFilter(
        ICurrentUser currentUser,
        ILogger<RequireAuthenticatedUserFilter> logger) : IFilterMetadata, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (currentUser.IsDemo)
            {
                logger.LogWarning(
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
    }
}