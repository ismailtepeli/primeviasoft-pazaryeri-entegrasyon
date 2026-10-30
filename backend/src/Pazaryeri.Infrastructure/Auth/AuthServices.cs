using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pazaryeri.Application.Auth;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Auth;

public static class AppClaims
{
    public const string TenantId = "tenant_id";
    public const string DealerId = "dealer_id";
    /// <summary>"admin" ya da "dealer"; eski yönetici token'larında yoktur ve yönetici sayılır.</summary>
    public const string Principal = "principal";
    public const string PrincipalAdmin = "admin";
    public const string PrincipalDealer = "dealer";
}

public static class AuthPolicies
{
    public const string Dealer = "Dealer";
}

public class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    public (string Token, DateTime ExpiresAt) CreateForDealer(DealerUser user, Dealer dealer) => DealerTokenFactory.Create(options.Value, user, dealer);

    public (string Token, DateTime ExpiresAt) Create(User user)
    {
        var o = options.Value;
        var expires = DateTime.UtcNow.AddMinutes(o.ExpiryMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(AppClaims.TenantId, user.TenantId.ToString()),
            new Claim(AppClaims.Principal, AppClaims.PrincipalAdmin),
        };
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(o.Issuer, o.Audience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}

public class DealerTokenFactory
{
    public static (string Token, DateTime ExpiresAt) Create(JwtOptions o, DealerUser user, Dealer dealer)
    {
        var expires = DateTime.UtcNow.AddMinutes(o.ExpiryMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(AppClaims.TenantId, user.TenantId.ToString()),
            new Claim(AppClaims.DealerId, dealer.Id.ToString()),
            new Claim(AppClaims.Principal, AppClaims.PrincipalDealer),
        };
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(o.Issuer, o.DealerAudience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}

public class AuthService(AppDbContext db, ITokenService tokens) : IAuthService
{
    private readonly PasswordHasher<User> _hasher = new();

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        // Giriş öncesi kiracı bilinmediği için kiracı filtresi bilerek atlanır.
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, ct);
        if (user is null) return null;

        var tenantActive = await db.Tenants.AnyAsync(t => t.Id == user.TenantId && t.IsActive, ct);
        if (!tenantActive) return null;

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed) return null;

        var (token, expires) = tokens.Create(user);
        return new LoginResponse(token, expires, new UserInfo(user.Id, user.TenantId, user.Email, user.FullName));
    }

}
