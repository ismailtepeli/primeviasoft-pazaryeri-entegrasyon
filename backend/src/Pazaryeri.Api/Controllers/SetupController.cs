using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;

namespace Pazaryeri.Api.Controllers;

public record SetupChannelsInput(List<string>? Channels);

/// <summary>Satış kanalı kurulumu: ilk giriş sihirbazı ve Ayarlar > Satış kanalları aynı uçları kullanır.</summary>
[ApiController]
[Route("api/setup")]
[CoreModule]
public class SetupController(ISetupService setup) : ControllerBase
{
    /// <summary>Kanallar (pazaryerleri, B2B, B2C), açık mı ve kurulum tamamlandı mı.</summary>
    [HttpGet]
    public Task<SetupState> Get(CancellationToken ct) => setup.GetAsync(ct);

    /// <summary>Seçilen kanalları açar, seçilmeyenleri (başka kanal kullanmıyorsa) kapatır ve kurulumu tamamlar.</summary>
    [HttpPut("channels")]
    public async Task<IActionResult> Apply(SetupChannelsInput input, CancellationToken ct)
    {
        if (input.Channels is null) return BadRequest(new { error = "Kanal listesi gerekli." });
        var outcome = await setup.ApplyAsync(input.Channels, ct);
        return outcome.Result switch
        {
            SetupResult.UnknownChannel => BadRequest(new { error = "Bilinmeyen kanal: " + outcome.Detail }),
            SetupResult.ChannelNotAvailable => Conflict(new { error = "not_available", message = $"{outcome.Detail} henüz hazır değil; yakında seçilebilecek." }),
            SetupResult.Failed => Conflict(new { error = "setup_failed", message = "Kanal değişikliği uygulanamadı: " + outcome.Detail }),
            _ => Ok(await setup.GetAsync(ct)),
        };
    }
}
