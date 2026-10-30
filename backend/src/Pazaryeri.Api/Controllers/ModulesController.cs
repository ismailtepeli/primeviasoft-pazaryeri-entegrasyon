using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;

namespace Pazaryeri.Api.Controllers;

public record ModuleToggleInput(bool? Enabled);

[ApiController]
[Route("api/modules")]
[CoreModule]
public class ModulesController(IModuleService modules) : ControllerBase
{
    /// <summary>Tüm modüller ve oturumdaki kiracı için açık/kapalı durumları.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<ModuleStatus>> List(CancellationToken ct) => await modules.ListAsync(ct);

    /// <summary>Modülü açar ya da kapatır; bağımlılık ve çekirdek kuralları uygulanır.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Set(string id, ModuleToggleInput input, CancellationToken ct)
    {
        if (input.Enabled is null) return BadRequest(new { error = "Açık mı kapalı mı olacağını belirtin." });
        var change = await modules.SetAsync(id, input.Enabled.Value, ct);
        return change.Result switch
        {
            ModuleChangeResult.Unknown => NotFound(new { error = "Bilinmeyen modül." }),
            ModuleChangeResult.CoreCannotDisable => Conflict(new { error = "core_cannot_disable", message = "Çekirdek modül kapatılamaz." }),
            ModuleChangeResult.MissingDependencies => Conflict(new { error = "missing_dependencies", modules = change.Related, message = "Önce şu modülleri açın: " + Names(change.Related) }),
            ModuleChangeResult.HasDependents => Conflict(new { error = "has_dependents", modules = change.Related, message = "Önce şu modülleri kapatın: " + Names(change.Related) }),
            _ => Ok(await modules.ListAsync(ct)),
        };
    }

    private static string Names(IEnumerable<string> ids) => string.Join(", ", ids.Select(i => ModuleCatalog.Find(i)?.Name ?? i));
}
