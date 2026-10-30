using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Catalog;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

/// <param name="Kind">"Categories", "Brands" veya "Attributes".</param>
public record CatalogRunDto(string Kind, DateTime? FinishedAt, int Added, int Changed, int Removed, string? Error);

/// <param name="Supported">Kanal için katalog senkronu uygulanmış mı.</param>
/// <param name="SyncInFlight">Bu kiracı için bekleyen ya da çalışan bir katalog senkronu var mı.</param>
public record CatalogStatusDto(string Channel, bool Supported, int Categories, int LeafCategories, int Brands, int AttributeCategories,
    bool SyncInFlight, IReadOnlyList<CatalogRunDto> LastRuns);

public record CatalogSyncStarted(bool Enqueued);

[ApiController]
[Route("api/catalog")]
[RequiresModule(ModuleIds.Marketplace)]
public class CatalogController(AppDbContext db, IJobQueue queue) : ControllerBase
{
    private static readonly SyncJobStatus[] InFlight = [SyncJobStatus.Pending, SyncJobStatus.Running, SyncJobStatus.Retrying];

    [HttpGet]
    public async Task<List<CatalogStatusDto>> Status(CancellationToken ct)
    {
        var result = new List<CatalogStatusDto>();
        foreach (var channel in CatalogWeeklySync.Channels) result.Add(await StatusOf(channel, ct));
        return result;
    }

    private async Task<CatalogStatusDto> StatusOf(string channel, CancellationToken ct)
    {
        var categories = db.ChannelCategories.Where(c => c.Channel == channel && c.IsActive);
        var runs = (await db.CatalogSyncRuns.AsNoTracking().Where(r => r.Channel == channel && r.FinishedAt != null)
                .OrderByDescending(r => r.FinishedAt).Take(60).ToListAsync(ct))
            .GroupBy(r => r.Kind).Select(g => g.First())
            .Select(r => new CatalogRunDto(r.Kind.ToString(), r.FinishedAt, r.Added, r.Changed, r.Removed, r.Error)).ToList();

        return new CatalogStatusDto(channel, true,
            await categories.CountAsync(ct), await categories.CountAsync(c => c.IsLeaf, ct),
            await db.ChannelBrands.CountAsync(b => b.Channel == channel && b.IsActive, ct),
            await db.ChannelAttributes.Where(a => a.Channel == channel && a.IsActive).Select(a => a.CategoryExternalId).Distinct().CountAsync(ct),
            await db.SyncJobs.AnyAsync(j => j.Type == SyncJobType.CatalogSync && j.Channel == channel && InFlight.Contains(j.Status), ct),
            runs);
    }

    /// <summary>Katalog senkronunu hemen kuyruğa alır; zaten bekleyen/çalışan varsa yenisi eklenmez.</summary>
    [HttpPost("{channel}/sync")]
    public async Task<ActionResult<CatalogSyncStarted>> Sync(string channel, CancellationToken ct)
    {
        if (!CatalogWeeklySync.Channels.Contains(channel)) return BadRequest(new { error = $"{channel} için katalog senkronu henüz desteklenmiyor." });

        var status = await db.ChannelConnections.Where(c => c.Channel == channel).Select(c => (ConnectionStatus?)c.Status).FirstOrDefaultAsync(ct);
        if (status != ConnectionStatus.Connected)
            return BadRequest(new { error = $"{channel} bağlı değil ya da doğrulanmadı; önce Mağazalar ekranından bağlantıyı test edin." });

        if (await db.SyncJobs.AnyAsync(j => j.Type == SyncJobType.CatalogSync && j.Channel == channel && InFlight.Contains(j.Status), ct))
            return new CatalogSyncStarted(false);

        await queue.EnqueueAsync(SyncJobType.CatalogSync, channel, ct: ct);
        return new CatalogSyncStarted(true);
    }
}
