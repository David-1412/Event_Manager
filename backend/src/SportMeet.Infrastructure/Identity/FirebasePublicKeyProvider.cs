using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SportMeet.Application.Ingestion;


namespace SportMeet.Infrastructure.Identity;

/// <summary>
/// Supplies the RSA keys Firebase signs ID tokens with, fetched from Google's public
/// JWKS endpoint and cached so a burst of authenticated requests costs one network
/// round trip rather than one per request.
///
/// Deliberately *not* the FirebaseAdmin SDK: that pulls the Google.Apis → net9.0
/// transitive line this net8 build is pinned away from (the same reason Microsoft.Graph
/// is held at 5.x). Verification is done with the framework's own
/// <c>Microsoft.IdentityModel.Tokens</c>, and the keys are fetched with the shared
/// <c>IHttpClientFactory</c>, so nothing new enters the dependency graph.
///
/// Google rotates these keys on a published cadence; the cache lifetime is well under
/// that, and a token whose <c>kid</c> is unknown forces a single forced refresh before
/// it is rejected, so a rotation in flight never fails legitimate sign-ins for long.
/// </summary>
public sealed class FirebasePublicKeyProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<FirebaseOptions> options,
    IMemoryCache cache)
{
    private const string CacheKey = "firebase.jwks";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    private readonly FirebaseOptions _options = options.Value;

    /// <summary>Every signing key currently published, plus the moment the set was
    /// captured so a stale read can be forced past the cache exactly once.</summary>
    private sealed record KeySet(DateTimeOffset FetchedAt, IReadOnlyDictionary<string, RsaSecurityKey> ByKid);

    /// <summary>Resolve the signing key for a token's <c>kid</c>. Returns null when the
    /// kid is not one Firebase currently publishes — the caller rejects the token.</summary>
    public async Task<RsaSecurityKey?> ResolveAsync(string? kid, CancellationToken ct = default)
    {
        var set = await GetSetAsync(force: false, ct);
        if (kid is not null && set.ByKid.TryGetValue(kid, out var key)) return key;

        // Unknown kid is the signature of a rotation we haven't observed yet, so refresh
        // past the cache once before giving up. If it is still absent, the token was not
        // signed by a current Firebase key.
        set = await GetSetAsync(force: true, ct);
        return kid is not null && set.ByKid.TryGetValue(kid, out var rotated) ? rotated : null;
    }

    /// <summary>The exact issuer for the configured project. Tokens are validated
    /// against this and the project id as audience, so a token minted for any other
    /// project — or a self-signed forgery — fails validation rather than being trusted.</summary>
    public string ValidIssuer => _options.ExpectedIssuer(_options.ProjectId);

    /// <summary>The expected audience, which for Firebase is the project id itself.</summary>
    public string ValidAudience => _options.ProjectId;

    private async Task<KeySet> GetSetAsync(bool force, CancellationToken ct)
    {
        if (!force && cache.TryGetValue(CacheKey, out KeySet? cached) && cached is not null
            && cached.FetchedAt > DateTimeOffset.UtcNow - CacheLifetime)
            return cached;

        var keys = await FetchAsync(ct);
        var set = new KeySet(DateTimeOffset.UtcNow, keys);
        cache.Set(CacheKey, set, CacheLifetime);
        return set;
    }

    private async Task<IReadOnlyDictionary<string, RsaSecurityKey>> FetchAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("firebase");
        using var response = await client.GetAsync(JwksUri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // This endpoint is a JSON object mapping kid -> PEM certificate, not a JWKS.
        // The public key is read off the cert, so there is no JWK/BigInt parsing here —
        // fewer bytes to get wrong and RS256 is exactly SHA-256 over RSA, which every
        // Firebase signing cert uses.
        var result = new Dictionary<string, RsaSecurityKey>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            RsaSecurityKey? key;
            try
            {
                using var cert = X509Certificate2.CreateFromPem(property.Value.GetString());
                using var rsa = cert.GetRSAPublicKey();
                if (rsa is null) continue;
                key = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = property.Name };
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // A single unparseable cert must not drop the whole key set; skip it and
                // let the rotation-refresh path recover if it was the one a token needs.
                continue;
            }
            result[property.Name] = key;
        }
        return result;
    }

    // Google publishes the signing certs for the securetoken issuer (Firebase's token
    // issuer) as kid -> PEM cert. The set is shared across projects; issuer/audience
    // validation is what scopes a token to *this* project, not the key set.
    private static string JwksUri =>
        "https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com";
}

