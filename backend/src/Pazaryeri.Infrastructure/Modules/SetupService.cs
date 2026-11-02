using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Modules;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Modules;

/// <summary>
/// Satış kanalı kurulumu: seçime göre modülleri bağımlılık sırasıyla açıp kapatır (<see cref="SalesChannelCatalog.Plan"/>).
/// Hiçbir adım uygulanmadan önce seçim doğrulanır; adımlar tek tek <see cref="IModuleService"/> kurallarından geçer.
/// </summary>
public class SetupService(AppDbContext db, IModuleService modules, ITenantProvider tenant, TimeProvider time) : ISetupService
{
    private Guid CurrentTenant => tenant.TenantId ?? throw new InvalidOperationException("Kiracı bilinmeden kurulum okunamaz.");

    public async Task<SetupState> GetAsync(CancellationToken ct = default)
    {
        var enabled = await modules.EnabledAsync(null, ct);
        var completed = await db.Tenants.AsNoTracking().Where(t => t.Id == CurrentTenant).Select(t => t.SetupCompletedAt != null).FirstOrDefaultAsync(ct);
        var channels = SalesChannelCatalog.All
            .Select(c => new SalesChannelStatus(c.Id, c.Name, c.Description, c.Available, SalesChannelCatalog.IsOn(c, enabled), c.Modules))
            .ToList();
        return new SetupState(completed, channels);
    }

    public async Task<SetupOutcome> ApplyAsync(IReadOnlyCollection<string> channels, CancellationToken ct = default)
    {
        var selected = new List<string>();
        foreach (var id in channels.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var def = SalesChannelCatalog.Find(id);
            if (def is null) return new(SetupResult.UnknownChannel, id);
            if (!def.Available) return new(SetupResult.ChannelNotAvailable, def.Name);
            selected.Add(def.Id);
        }

        var enabled = await modules.EnabledAsync(null, ct);
        var plan = SalesChannelCatalog.Plan(selected, enabled);
        // Önce kapatılır (bağımlılar önce), sonra açılır (bağımlılıklar önce); böylece ara adımlar da kurallara uyar.
        foreach (var id in plan.Disable)
            if (await modules.SetAsync(id, false, ct) is { Result: not (ModuleChangeResult.Changed or ModuleChangeResult.Unchanged) } fail)
                return new(SetupResult.Failed, $"{id}: {fail.Result}");
        foreach (var id in plan.Enable)
            if (await modules.SetAsync(id, true, ct) is { Result: not (ModuleChangeResult.Changed or ModuleChangeResult.Unchanged) } fail)
                return new(SetupResult.Failed, $"{id}: {fail.Result}");

        var now = time.GetUtcNow().UtcDateTime;
        await db.Tenants.Where(t => t.Id == CurrentTenant && t.SetupCompletedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.SetupCompletedAt, now), ct);
        return new(SetupResult.Applied);
    }
}
