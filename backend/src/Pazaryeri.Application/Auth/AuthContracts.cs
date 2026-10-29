using Pazaryeri.Domain;

namespace Pazaryeri.Application.Auth;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string AccessToken, DateTime ExpiresAt, UserInfo User);

public record UserInfo(Guid Id, Guid TenantId, string Email, string FullName);

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) Create(User user);
}

public interface IAuthService
{
    /// <summary>E-posta ve parola doğruysa token döner; aksi halde null.</summary>
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct);
}

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "pazaryeri";
    public string Audience { get; set; } = "pazaryeri";
    /// <summary>En az 32 karakter. Üretimde ortam değişkeni veya gizli depodan gelmeli.</summary>
    public string Key { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;
}
