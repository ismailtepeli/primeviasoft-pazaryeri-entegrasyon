using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Shop;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Shop;

namespace Pazaryeri.Api.Controllers;

public record GoogleConfig(string? ClientId);

/// <summary>B2C mağaza müşteri girişi (api/shop/auth): kayıt, giriş, Google. Herkese açık ve hız sınırlıdır; kiracı slug ile verilir.</summary>
[ApiController]
[Route("api/shop/auth")]
[AllowAnonymous]
[CoreModule]
[EnableRateLimiting(StoreRateLimiting.AuthPolicy)]
public class ShopAuthController(CustomerAuthService auth) : ControllerBase
{
    private IActionResult Map(CustomerAuthOutcome<CustomerSession> r) => r.Result switch
    {
        CustomerAuthResult.Ok => Ok(r.Value),
        CustomerAuthResult.InvalidInput => BadRequest(new { error = r.Error }),
        CustomerAuthResult.TermsRequired => BadRequest(new { error = r.Error, code = "terms_required" }),
        CustomerAuthResult.Duplicate => Conflict(new { error = r.Error }),
        CustomerAuthResult.GoogleNotConfigured => Conflict(new { error = r.Error, code = "google_not_configured" }),
        CustomerAuthResult.InvalidGoogleToken or CustomerAuthResult.EmailNotVerified => Unauthorized(new { error = r.Error }),
        CustomerAuthResult.Inactive => StatusCode(StatusCodes.Status403Forbidden, new { error = r.Error }),
        CustomerAuthResult.UnknownShop => NotFound(new { error = "Mağaza bulunamadı." }),
        _ => Unauthorized(new { error = "E-posta veya parola hatalı." }),
    };

    /// <summary>Google düğmesi için herkese açık istemci kimliği; yapılandırılmamışsa boş döner (düğme gizlenir).</summary>
    [HttpGet("config")]
    public async Task<IActionResult> Config([FromQuery] string? slug, CancellationToken ct) => Ok(new GoogleConfig(await auth.GoogleClientIdAsync(slug, ct)));

    [HttpPost("register")]
    public async Task<IActionResult> Register(CustomerRegister input, CancellationToken ct) => Map(await auth.RegisterAsync(input, ct));

    [HttpPost("login")]
    public async Task<IActionResult> Login(CustomerLogin input, CancellationToken ct) => Map(await auth.LoginAsync(input, ct));

    [HttpPost("google")]
    public async Task<IActionResult> Google(CustomerGoogleLogin input, CancellationToken ct) => Map(await auth.GoogleLoginAsync(input, ct));
}

/// <summary>Oturum açmış B2C müşterisinin hesabı (api/shop/me): profil, parola, adres defteri ve siparişleri. Yalnızca müşteri token'ı girebilir.</summary>
[ApiController]
[Route("api/shop/me")]
[Authorize(Policy = AuthPolicies.Customer)]
[RequiresModule(ModuleIds.B2CStore)]
public class ShopAccountController(CustomerAccountService account) : ControllerBase
{
    private Guid CustomerId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (CustomerException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }

    private async Task<IActionResult> Run(Func<Task> action)
    {
        try { await action(); return NoContent(); }
        catch (CustomerException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }

    [HttpGet]
    public Task<ActionResult<CustomerProfile>> Profile(CancellationToken ct) => Run(() => account.GetProfileAsync(CustomerId, ct));

    [HttpPut]
    public Task<ActionResult<CustomerProfile>> UpdateProfile(ProfileUpdate input, CancellationToken ct) => Run(() => account.UpdateProfileAsync(CustomerId, input, ct));

    [HttpPut("password")]
    [EnableRateLimiting(StoreRateLimiting.AuthPolicy)]
    public Task<IActionResult> ChangePassword(PasswordChange input, CancellationToken ct) => Run(() => account.ChangePasswordAsync(CustomerId, input, ct));

    [HttpGet("addresses")]
    public Task<ActionResult<IReadOnlyList<AddressDto>>> Addresses(CancellationToken ct) => Run(() => account.AddressesAsync(CustomerId, ct));

    [HttpPost("addresses")]
    public Task<ActionResult<AddressDto>> AddAddress(AddressInput input, CancellationToken ct) => Run(() => account.AddAddressAsync(CustomerId, input, ct));

    [HttpPut("addresses/{id:guid}")]
    public Task<ActionResult<AddressDto>> UpdateAddress(Guid id, AddressInput input, CancellationToken ct) => Run(() => account.UpdateAddressAsync(CustomerId, id, input, ct));

    [HttpDelete("addresses/{id:guid}")]
    public Task<IActionResult> DeleteAddress(Guid id, CancellationToken ct) => Run(() => account.DeleteAddressAsync(CustomerId, id, ct));

    [HttpGet("orders")]
    public Task<ActionResult<CustomerOrderPage>> Orders([FromQuery] int page = 1, CancellationToken ct = default) => Run(() => account.OrdersAsync(CustomerId, page, ct));

    [HttpGet("orders/{id:guid}")]
    public Task<ActionResult<CustomerOrderDetail>> Order(Guid id, CancellationToken ct) => Run(() => account.OrderAsync(CustomerId, id, ct));
}
