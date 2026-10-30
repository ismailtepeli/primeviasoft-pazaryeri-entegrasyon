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
