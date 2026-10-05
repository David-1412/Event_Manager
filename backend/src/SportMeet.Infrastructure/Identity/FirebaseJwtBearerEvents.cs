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
