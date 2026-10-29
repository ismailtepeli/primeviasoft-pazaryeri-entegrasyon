using Pazaryeri.Domain;

namespace Pazaryeri.Application.Jobs;

/// <summary>Kuyruğa giden mesaj: yalnızca iş kimliği taşır, ayrıntılar SyncJob tablosundadır.</summary>
public record SyncJobMessage(Guid JobId, Guid TenantId);

public interface IJobQueue
{
    /// <summary>Oturumdaki kiracı için SyncJob kaydı açar ve kuyruğa atar.</summary>
    Task<SyncJob> EnqueueAsync(SyncJobType type, string channel, string? batchId = null, CancellationToken ct = default);
}

/// <summary>Bir iş türünü işleyen sınıf. Yeni senkron özellikleri bunu uygulayıp kaydolur.</summary>
public interface ISyncJobHandler
{
    SyncJobType Type { get; }
    Task HandleAsync(SyncJob job, CancellationToken ct);
}

public class RabbitMqOptions
{
    public const string Section = "RabbitMq";
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    /// <summary>Bir iş en fazla bu kadar denenir; sonra ölü mektup kuyruğuna gider.</summary>
    public int MaxAttempts { get; set; } = 5;
    public int RetryDelaySeconds { get; set; } = 30;
    public ushort Prefetch { get; set; } = 5;
}
