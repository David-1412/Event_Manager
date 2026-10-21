using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace SportMeet.Infrastructure.Identity;

/// <summary>
/// Wires Firebase ID-token validation into ASP.NET Core's bearer handler.
///
/// The API trusts the browser's raw Firebase ID token (no exchanged API token).
/// Signature, expiry, issuer and audience are validated by the JwtBearer handler;
/// the signing key resolver supplies the key before signature validation. This class
/// stamps the verified identity onto the principal so <see cref="TokenCurrentUser"/>
/// can resolve the user without trusting unverified claims.
/// </summary>
public sealed class FirebaseJwtBearerEvents : JwtBearerEvents
{
    /// <summary>Claim carrying the Firebase UID (the token's sub), stamped after the
    /// signature has been verified.</summary>
    public const string AuthUidClaimType = ".firebase.uid";

    /// <summary>Paths that may carry the token in the query string.</summary>
    public const string HubPathPrefix = "/hubs/";

    /// <summary>
    /// Browsers cannot set an Authorization header on a WebSocket, so SignalR's
    /// client sends the token as <c>?access_token=</c>. It is accepted only on the hub
    /// paths, so no ordinary API route can be authenticated from a URL - a link or a
    /// log line can then never carry a usable credential for the REST API. Production
    /// logs ASP.NET Core at Warning, so hub URLs (with their token) are not logged.
    /// </summary>
    public override Task MessageReceived(MessageReceivedContext context)
    {
        var token = context.Request.Query["access_token"].ToString();
        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(HubPathPrefix.TrimEnd('/')))
        {
            context.Token = token;
        }
        return base.MessageReceived(context);
    }

    public override Task TokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var uid = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? principal?.FindFirst("sub")?.Value;
        if (principal is null || string.IsNullOrWhiteSpace(uid))
        {
            context.Fail("The validated Firebase token did not contain a subject UID.");
            return Task.CompletedTask;
        }

        principal.AddIdentity(new ClaimsIdentity(new[]
        {
            new Claim(AuthUidClaimType, uid),
            new Claim(ClaimTypes.Email, FindClaim(principal, ClaimTypes.Email, "email")),
            new Claim(ClaimTypes.Name, FindClaim(principal, ClaimTypes.Name, "name")),
            new Claim("picture", FindClaim(principal, "picture")),
        }, authenticationType: JwtBearerDefaults.AuthenticationScheme));

        return base.TokenValidated(context);
    }

    private static string FindClaim(ClaimsPrincipal principal, params string[] types)
    {
        foreach (var type in types)
        {
            var value = principal.FindFirst(type)?.Value;
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return string.Empty;
    }
}
