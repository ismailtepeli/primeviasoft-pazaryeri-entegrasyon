using System.Text.Json;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using RabbitMQ.Client;

namespace Pazaryeri.Infrastructure.Messaging;

public class JobQueue(AppDbContext db, RabbitMqConnection rabbit, ITenantProvider tenant) : IJobQueue
{
    public async Task<SyncJob> EnqueueAsync(SyncJobType type, string channel, string? batchId = null, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Kiracı bilinmeden iş kuyruğa alınamaz.");

        var job = new SyncJob { TenantId = tenantId, Type = type, Channel = channel, BatchId = batchId };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync(ct);

        await PublishAsync(rabbit, RabbitMqTopology.JobsKey, new SyncJobMessage(job.Id, tenantId), retryCount: 0, ct);
        return job;
    }

    public static async Task PublishAsync(RabbitMqConnection rabbit, string routingKey, SyncJobMessage message, int retryCount, CancellationToken ct)
    {
        var connection = await rabbit.GetAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Headers = new Dictionary<string, object?> { [RabbitMqTopology.RetryCountHeader] = retryCount },
        };
        await channel.BasicPublishAsync(RabbitMqTopology.Exchange, routingKey, mandatory: false, props,
            JsonSerializer.SerializeToUtf8Bytes(message), ct);
    }
}
