using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pazaryeri.Application.Shop;

namespace Pazaryeri.Infrastructure.Shop;

public class GoogleOptions
{
    public const string Section = "Google";
    /// <summary>Google Cloud'da oluşturulan OAuth web istemcisi kimliği (herkese açık değerdir). Boşsa "Google ile giriş" kapalıdır.</summary>
    public string? ClientId { get; set; }
}

/// <summary>
/// Google Identity Services'in verdiği kimlik belirtecini (ID token) doğrular: Google'ın herkese açık anahtarlarıyla RS256 imzası,
/// yayıncı (accounts.google.com), hedef kitle (bizim istemci kimliğimiz) ve süre denetlenir. Anahtarlar bir saat önbelleğe alınır;
/// bilinmeyen anahtar kimliği gelirse (anahtar dönüşü) bir kez yenilenir. Hiçbir sır tutmaz; yalnızca istemci kimliği gerekir.
/// </summary>
public class GoogleIdTokenVerifier(HttpClient http, IOptions<GoogleOptions> options, IMemoryCache cache) : IGoogleIdTokenVerifier
{
    public const string CertsUrl = "https://www.googleapis.com/oauth2/v3/certs";
    private const string CacheKey = "google:jwks";
    private static readonly string[] Issuers = ["https://accounts.google.com", "accounts.google.com"];

    public string? ClientId => string.IsNullOrWhiteSpace(options.Value.ClientId) ? null : options.Value.ClientId.Trim();

    public bool Configured => ClientId is not null;

    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        if (!Configured || string.IsNullOrWhiteSpace(idToken) || idToken.Length > 4096) return null;
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        if (!handler.CanReadToken(idToken)) return null;

        for (var refresh = false; ; refresh = true)
        {
            var keys = await KeysAsync(refresh, ct);
            if (keys is null) return null;
            var parameters = new TokenValidationParameters
            {
                ValidIssuers = Issuers,
                ValidAudience = ClientId,
                IssuerSigningKeys = keys,
                RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            };
            try
            {
                var principal = handler.ValidateToken(idToken, parameters, out _);
                var sub = principal.FindFirst("sub")?.Value;
                var email = principal.FindFirst("email")?.Value;
                if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email)) return null;
                return new GoogleIdentity(sub, email.Trim().ToLowerInvariant(), string.Equals(principal.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                    principal.FindFirst("name")?.Value, principal.FindFirst("picture")?.Value);
            }
            catch (SecurityTokenSignatureKeyNotFoundException) when (!refresh) { /* anahtar dönmüş olabilir: bir kez yenile */ }
            catch (SecurityTokenException) { return null; }
            catch (ArgumentException) { return null; }
        }
    }

    private async Task<IList<SecurityKey>?> KeysAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && cache.TryGetValue(CacheKey, out IList<SecurityKey>? hit) && hit is not null) return hit;
        try
        {
            using var response = await http.GetAsync(CertsUrl, ct);
            if (!response.IsSuccessStatusCode) return null;
            var keys = new JsonWebKeySet(await response.Content.ReadAsStringAsync(ct)).GetSigningKeys();
            if (keys.Count == 0) return null;
            cache.Set(CacheKey, keys, TimeSpan.FromHours(1));
            return keys;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (ArgumentException) { return null; }
    }
}
