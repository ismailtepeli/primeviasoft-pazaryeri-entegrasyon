using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Modules;

/// <summary>Kiracı başına modül durumu. Okuma kısa süre önbelleklenir; aç/kapa önbelleği hemen geçersiz kılar.</summary>
public class ModuleService(AppDbContext db, ITenantProvider tenant, IMemoryCache cache, TimeProvider time) : IModuleService
{
    public static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(30);

    private Guid CurrentTenant => tenant.TenantId ?? throw new InvalidOperationException("Kiracı bilinmeden modül durumu okunamaz.");

    public async Task<IReadOnlySet<string>> EnabledAsync(Guid? tenantId = null, CancellationToken ct = default)
    {
        var id = tenantId ?? CurrentTenant;
        if (cache.TryGetValue(Key(id), out HashSet<string>? hit) && hit is not null) return hit;
        var saved = await db.TenantModules.IgnoreQueryFilters().AsNoTracking().Where(m => m.TenantId == id).ToDictionaryAsync(m => m.ModuleId, m => m.Enabled, ct);
        var enabled = ModuleCatalog.Resolve(saved);
        cache.Set(Key(id), enabled, CacheTime);
        return enabled;
    }

    public async Task<bool> IsEnabledAsync(string moduleId, Guid? tenantId = null, CancellationToken ct = default) =>
        (await EnabledAsync(tenantId, ct)).Contains(moduleId);

    public async Task<IReadOnlyList<ModuleStatus>> ListAsync(CancellationToken ct = default)
    {
        var enabled = await EnabledAsync(null, ct);
        return ModuleCatalog.All.Select(m => new ModuleStatus(m.Id, m.Name, m.Description, m.Core, enabled.Contains(m.Id), m.DependsOn)).ToList();
    }

    public async Task<ModuleChange> SetAsync(string moduleId, bool enable, CancellationToken ct = default)
    {
        var def = ModuleCatalog.Find(moduleId);
        if (def is null) return new(ModuleChangeResult.Unknown, []);
        var tenantId = CurrentTenant;
        cache.Remove(Key(tenantId)); // karar için güncel durum okunur
        var enabled = await EnabledAsync(tenantId, ct);
        if (enabled.Contains(moduleId) == enable) return new(ModuleChangeResult.Unchanged, []);

        if (!enable)
        {
            if (def.Core) return new(ModuleChangeResult.CoreCannotDisable, []);
            var dependents = ModuleCatalog.EnabledDependents(moduleId, enabled);
            if (dependents.Count > 0) return new(ModuleChangeResult.HasDependents, dependents);
        }
        else
        {
            var missing = ModuleCatalog.MissingDependencies(moduleId, enabled);
            if (missing.Count > 0) return new(ModuleChangeResult.MissingDependencies, missing);
        }

        var row = await db.TenantModules.FirstOrDefaultAsync(m => m.ModuleId == moduleId, ct);
        if (row is null) db.TenantModules.Add(row = new TenantModule { ModuleId = moduleId });
        row.Enabled = enable;
        row.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        cache.Remove(Key(tenantId));
        return new(ModuleChangeResult.Changed, []);
    }

    private static string Key(Guid tenantId) => $"modules:{tenantId}";
}
