using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

public record SyncJobDto(Guid Id, string Type, string Channel, string Status, string? BatchId, string? Error,
    int Attempts, DateTime CreatedAt, DateTime? StartedAt, DateTime? FinishedAt);

[ApiController]
[Route("api/sync-jobs")]
[CoreModule]
public class SyncJobsController(AppDbContext db) : ControllerBase
{
    /// <summary>Oturumdaki kiracının son senkron işleri (kiracı filtresi otomatik uygulanır).</summary>
    [HttpGet]
    public async Task<List<SyncJobDto>> List([FromQuery] SyncJobStatus? status, CancellationToken ct) =>
        await db.SyncJobs
            .Where(j => status == null || j.Status == status)
            .OrderByDescending(j => j.CreatedAt)
            .Take(100)
            .Select(j => new SyncJobDto(j.Id, j.Type.ToString(), j.Channel, j.Status.ToString(), j.BatchId, j.Error,
                j.Attempts, j.CreatedAt, j.StartedAt, j.FinishedAt))
            .ToListAsync(ct);
}
