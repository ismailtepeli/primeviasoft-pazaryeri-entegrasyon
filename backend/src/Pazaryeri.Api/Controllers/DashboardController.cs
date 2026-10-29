using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

/// <param name="Status">"Connected", "AuthError" veya "NotConnected".</param>
/// <param name="LastSyncAt">Kanaldaki son başarılı senkronun bitiş zamanı (UTC); hiç yoksa null.</param>
public record ChannelStatusDto(string Channel, string Status, DateTime? LastSyncAt);

[ApiController]
[Route("api/dashboard")]
public class DashboardController(AppDbContext db) : ControllerBase
{
    public const string NotConnected = "NotConnected";

    /// <summary>Desteklenen her kanal için bağlantı durumu ve son senkron zamanı.</summary>
    [HttpGet("channels")]
    public async Task<List<ChannelStatusDto>> Channels(CancellationToken ct)
    {
        var connections = await db.ChannelConnections.ToDictionaryAsync(c => c.Channel, c => c.Status, ct);
        var lastSync = await db.SyncJobs
            .Where(j => j.Status == SyncJobStatus.Succeeded && j.FinishedAt != null)
            .GroupBy(j => j.Channel)
            .Select(g => new { Channel = g.Key, At = g.Max(j => j.FinishedAt) })
            .ToDictionaryAsync(x => x.Channel, x => x.At, ct);

        return SupportedChannels.All
            .Select(ch => new ChannelStatusDto(
                ch,
                connections.TryGetValue(ch, out var status) ? status.ToString() : NotConnected,
                lastSync.GetValueOrDefault(ch)))
            .ToList();
    }
}
