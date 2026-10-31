using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Modules;
using Pazaryeri.Infrastructure.Auth;

namespace Pazaryeri.Api.Controllers;

/// <summary>Bayi sitesi kimlik doğrulaması (api/store). Yönetici girişinden ayrıdır; bayi token'ı yönetim API'sine giremez.</summary>
[ApiController]
[Route("api/store/auth")]
[RequiresModule(ModuleIds.Dealers)]
public class StoreAuthController(IDealerAuthService auth) : ControllerBase
{
    /// <summary>Bayi sitesi açılırken: firma adı ve sitenin hizmet verip vermediği. Kısa süre önbelleğe alınabilir.</summary>
    [AllowAnonymous]
    [CoreModule]
    [EnableRateLimiting(StoreRateLimiting.InfoPolicy)]
    [HttpGet("info")]
    public async Task<IActionResult> Info([FromQuery] string? slug, CancellationToken ct)
    {
        var info = await auth.InfoAsync(slug, ct);
        if (info is null) return NotFound(new { error = "Bayi sitesi bulunamadı." });
        Response.Headers.CacheControl = "public, max-age=30";
        return Ok(info);
    }

    [AllowAnonymous]
    [EnableRateLimiting(StoreRateLimiting.AuthPolicy)]
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(DealerApplication application, CancellationToken ct)
    {
        var result = await auth.ApplyAsync(application, ct);
        return result.Result switch
        {
            DealerAuthResult.Ok => Accepted(new { message = "Başvurunuz alındı. Onaylandığında giriş yapabilirsiniz." }),
            DealerAuthResult.InvalidInput => BadRequest(new { error = result.Error }),
            DealerAuthResult.Duplicate => Conflict(new { error = result.Error }),
            _ => NotFound(new { error = "Bayi sitesi bulunamadı." }),
        };
    }

    [AllowAnonymous]
    [EnableRateLimiting(StoreRateLimiting.AuthPolicy)]
    [HttpPost("login")]
    public async Task<IActionResult> Login(DealerLogin login, CancellationToken ct)
    {
        var result = await auth.LoginAsync(login, ct);
        return result.Result switch
        {
            DealerAuthResult.Ok => Ok(result.Value),
            DealerAuthResult.InvalidInput => BadRequest(new { error = result.Error }),
            DealerAuthResult.Pending => StatusCode(StatusCodes.Status403Forbidden, new { error = result.Error, code = "dealer_pending" }),
            DealerAuthResult.Passive => StatusCode(StatusCodes.Status403Forbidden, new { error = result.Error, code = "dealer_passive" }),
            _ => Unauthorized(new { error = "E-posta veya parola hatalı." }),
        };
    }

    /// <summary>Oturum açmış bayi kullanıcısı parolasını değiştirir.</summary>
    [Authorize(Policy = AuthPolicies.Dealer)]
    [EnableRateLimiting(StoreRateLimiting.AuthPolicy)]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(DealerPasswordChange change, CancellationToken ct)
    {
        var result = await auth.ChangePasswordAsync(Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!), change, ct);
        return result.Result switch
        {
            DealerAuthResult.Ok => Ok(new { message = "Parolanız değiştirildi." }),
            DealerAuthResult.InvalidInput => BadRequest(new { error = result.Error }),
            _ => BadRequest(new { error = result.Error ?? "Mevcut parola hatalı." }),
        };
    }

    /// <summary>Parola sıfırlama bağlantısı ister. E-posta kayıtlı olsun olmasın aynı yanıtı döner.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(StoreRateLimiting.AuthPolicy)]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(DealerPasswordResetRequest request, CancellationToken ct)
    {
        var result = await auth.RequestPasswordResetAsync(request, ct);
        return result.Result switch
        {
            DealerAuthResult.InvalidInput => BadRequest(new { error = result.Error }),
            DealerAuthResult.UnknownTenant => NotFound(new { error = "Bayi sitesi bulunamadı." }),
            _ => Ok(new { message = "E-posta kayıtlıysa parola sıfırlama bağlantısı gönderildi." }),
        };
    }

    /// <summary>E-postadaki token ile yeni parola belirler.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(StoreRateLimiting.AuthPolicy)]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(DealerPasswordResetConfirm confirm, CancellationToken ct)
    {
        var result = await auth.ResetPasswordAsync(confirm, ct);
        return result.Result switch
        {
            DealerAuthResult.Ok => Ok(new { message = "Parolanız güncellendi. Yeni parolanızla giriş yapabilirsiniz." }),
            DealerAuthResult.InvalidInput => BadRequest(new { error = result.Error }),
            _ => BadRequest(new { error = result.Error, code = "invalid_token" }),
        };
    }

    [Authorize(Policy = AuthPolicies.Dealer)]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!),
        dealerId = Guid.Parse(User.FindFirstValue(AppClaims.DealerId)!),
        email = User.FindFirstValue(JwtRegisteredClaimNames.Email),
        name = User.Identity!.Name,
    });
}
