using Microsoft.AspNetCore.RateLimiting;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Application.Auth;
using Pazaryeri.Infrastructure.Auth;

namespace Pazaryeri.Api.Controllers;

[ApiController]
[Route("api/auth")]
[CoreModule]
public class AuthController(IAuthService auth) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(StoreRateLimiting.AuthPolicy)] // parola denemesine karşı: istemci başına dakikada 10
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            return BadRequest(new { error = "E-posta ve parola zorunlu." });

        var result = await auth.LoginAsync(request, ct);
        return result is null ? Unauthorized(new { error = "E-posta veya parola hatalı." }) : result;
    }

    [Authorize]
    [HttpGet("me")]
    public UserInfo Me() => new(
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!),
        Guid.Parse(User.FindFirstValue(AppClaims.TenantId)!),
        User.FindFirstValue(JwtRegisteredClaimNames.Email)!,
        User.Identity!.Name!);
}
