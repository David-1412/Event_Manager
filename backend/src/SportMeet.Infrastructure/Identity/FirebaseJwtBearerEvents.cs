using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace SportMeet.Infrastructure.Identity;

/// <summary>
/// Wires Firebase ID-token validation into ASP.NET Core's bearer handler.
///
/// The API trusts the browser's raw Firebase ID token (no exchanged API token).
/// Everything framework-standard - signature, expiry, issuer, audience - is delegated
/// to the JwtBearer handler; this class only supplies the signing key chosen by the
/// token's kid (via <see cref="FirebasePublicKeyProvider"/>) and stamps the verified
/// identity onto the principal so <see cref="TokenCurrentUser"/> can read it without
/// resolving a database row during token validation.
/// </summary>
public sealed class FirebaseJwtBearerEvents : JwtBearerEvents
{
    /// <summary>Claim carrying the Firebase UID (the token's sub), stamped after the
    /// signature has been verified.</summary>
    public const string AuthUidClaimType = ".firebase.uid";

    private readonly FirebasePublicKeyProvider _keys;

    public FirebaseJwtBearerEvents(FirebasePublicKeyProvider keys)
    {
        _keys = keys;
    }

    public override async Task TokenValidated(TokenValidatedContext context)

    {
        var jwt = context.SecurityToken as JwtSecurityToken;
        var key = await _keys.ResolveAsync(jwt?.Header.Kid, context.HttpContext.RequestAborted);
        if (jwt is null || key is null)
        {
            // Signature could not be checked against any current Firebase key; reject
            // rather than trust an unverified token.
            context.Fail(new SecurityTokenSignatureKeyNotFoundException(
                "No Firebase signing key matched the token's kid."));
            return;
        }

        context.Principal?.AddIdentity(new ClaimsIdentity(new[]
        {
            new Claim(AuthUidClaimType, jwt.Subject),
            new Claim(ClaimTypes.Email, ClaimValue(jwt, "email")),
            new Claim(ClaimTypes.Name, ClaimValue(jwt, "name")),
            new Claim("picture", ClaimValue(jwt, "picture")),
        }, authenticationType: JwtBearerDefaults.AuthenticationScheme));

        await base.TokenValidated(context);
    }

    private static string ClaimValue(JwtSecurityToken jwt, string type)
    {
        var claim = jwt.Claims.FirstOrDefault(c => c.Type == type);
        return claim?.Value ?? string.Empty;
    }
}
