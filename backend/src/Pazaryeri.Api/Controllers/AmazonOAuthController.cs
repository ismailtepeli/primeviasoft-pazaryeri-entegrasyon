using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Pazaryeri.Application.Amazon;

namespace Pazaryeri.Api.Controllers;

public record AmazonAuthorizeUrl(string Url);

[ApiController]
public class AmazonOAuthController(IAmazonOAuthService oauth, IOptions<AmazonLwaOptions> options) : ControllerBase
{
    /// <summary>Yetki durumu, ayarların tanımlılığı ve client secret yenileme hatırlatıcısı.</summary>
    [HttpGet("api/channels/Amazon/oauth/status")]
    public Task<AmazonOAuthStatus> Status(CancellationToken ct) => oauth.GetStatusAsync(ct);

    /// <summary>"Amazon'da yetki ver" için Seller Central onay sayfasının adresi.</summary>
    [HttpPost("api/channels/Amazon/oauth/start")]
    public ActionResult<AmazonAuthorizeUrl> Start()
    {
        try { return new AmazonAuthorizeUrl(oauth.BuildAuthorizationUrl()); }
        catch (AmazonOAuthException e) { return BadRequest(new { error = e.Message }); }
    }

    /// <summary>
    /// Amazon'un tarayıcıyı yönlendirdiği adres; oturum (Bearer) taşımaz, kiracıyı state'ten öğrenir.
    /// İşlem bitince kullanıcı Mağazalar ekranına döner.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("api/oauth/amazon/callback")]
    public async Task<IActionResult> Callback(
        [FromQuery(Name = "spapi_oauth_code")] string? code,
        [FromQuery] string? state,
        [FromQuery(Name = "selling_partner_id")] string? sellingPartnerId,
        CancellationToken ct)
    {
        var back = $"{options.Value.FrontendBaseUrl.TrimEnd('/')}/magazalar?kanal=Amazon";
        try
        {
            await oauth.CompleteAsync(code, state, sellingPartnerId, ct);
            return Redirect($"{back}&oauth=ok");
        }
        catch (AmazonOAuthException e)
        {
            return Redirect($"{back}&oauth=error&mesaj={Uri.EscapeDataString(e.Message)}");
        }
    }
}
