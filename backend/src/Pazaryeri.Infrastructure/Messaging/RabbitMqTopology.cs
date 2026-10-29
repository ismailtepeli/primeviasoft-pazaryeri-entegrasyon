using Pazaryeri.Application.Jobs;
using RabbitMQ.Client;

namespace Pazaryeri.Infrastructure.Messaging;

/// <summary>
/// Kuyruk düzeni:
///   sync.jobs        ana kuyruk (işçi buradan okur)
///   sync.jobs.retry  bekleme kuyruğu; süresi dolan mesaj TTL ile ana kuyruğa geri döner
///   sync.jobs.dlq    ölü mektup kuyruğu; deneme hakkı biten veya işlenemeyen mesajlar
/// </summary>
public static class RabbitMqTopology
{
    public const string Exchange = "sync.exchange";
    public const string JobsQueue = "sync.jobs";
    public const string RetryQueue = "sync.jobs.retry";
    public const string DeadLetterQueue = "sync.jobs.dlq";

    public const string JobsKey = "jobs";
    public const string RetryKey = "retry";
    public const string DeadKey = "dead";

    public const string RetryCountHeader = "x-retry-count";

    public static async Task DeclareAsync(IChannel channel, RabbitMqOptions o, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, cancellationToken: ct);

        // Okunamayan (nack ile reddedilen) mesajlar da ölü mektup kuyruğuna düşer.
        await channel.QueueDeclareAsync(JobsQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = Exchange,
                ["x-dead-letter-routing-key"] = DeadKey,
            }, cancellationToken: ct);

        await channel.QueueDeclareAsync(RetryQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = o.RetryDelaySeconds * 1000,
                ["x-dead-letter-exchange"] = Exchange,
                ["x-dead-letter-routing-key"] = JobsKey,
            }, cancellationToken: ct);

        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);

        await channel.QueueBindAsync(JobsQueue, Exchange, JobsKey, cancellationToken: ct);
        await channel.QueueBindAsync(RetryQueue, Exchange, RetryKey, cancellationToken: ct);
        await channel.QueueBindAsync(DeadLetterQueue, Exchange, DeadKey, cancellationToken: ct);
    }
}
