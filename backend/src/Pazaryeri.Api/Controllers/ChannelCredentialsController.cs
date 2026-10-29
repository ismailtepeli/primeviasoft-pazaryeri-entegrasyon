using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Domain;

namespace Pazaryeri.Api.Controllers;

/// <param name="Environment">"Live" veya "Staging" (büyük/küçük harf fark etmez).</param>
/// <param name="Fields">Alan adı → değer. Boş gizli alan, kayıtlıysa korunur.</param>
public record SaveCredentialsRequest(string Environment, Dictionary<string, string> Fields);

[ApiController]
[Route("api/channels/{channel}/credentials")]
public class ChannelCredentialsController(IChannelCredentialService credentials) : ControllerBase
{
    /// <summary>Maskeli görünüm: gizli alanlar yalnızca "••••1234" biçiminde döner.</summary>
    [HttpGet]
    public async Task<ActionResult<CredentialView>> Get(string channel, [FromQuery] string environment = "Live", CancellationToken ct = default)
    {
        if (!Enum.TryParse<ChannelEnvironment>(environment, ignoreCase: true, out var env))
            return BadRequest(new { error = "Ortam 'Live' veya 'Staging' olmalı." });
        var view = await credentials.GetMaskedAsync(channel, env, ct);
        return view is null ? NotFound() : view;
    }

    [HttpPut]
    public async Task<ActionResult<CredentialView>> Save(string channel, SaveCredentialsRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<ChannelEnvironment>(request.Environment, ignoreCase: true, out var env))
            return BadRequest(new { error = "Ortam 'Live' veya 'Staging' olmalı." });
        try
        {
            return await credentials.SaveAsync(channel, env, request.Fields ?? [], ct);
        }
        catch (CredentialValidationException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }
}
