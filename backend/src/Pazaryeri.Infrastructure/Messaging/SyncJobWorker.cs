using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Pazaryeri.Infrastructure.Messaging;

/// <summary>sync.jobs kuyruğunu dinler, her mesaj için ilgili ISyncJobHandler'ı çalıştırır.</summary>
public class SyncJobWorker(
    RabbitMqConnection rabbit,
    IServiceScopeFactory scopes,
    ILogger<SyncJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = rabbit.Options;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = await rabbit.GetAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, o.Prefetch, false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, ea) => OnMessageAsync(channel, ea, o, stoppingToken);
                await channel.BasicConsumeAsync(RabbitMqTopology.JobsQueue, autoAck: false, consumer, stoppingToken);
                logger.LogInformation("Senkron işçisi {Queue} kuyruğunu dinliyor.", RabbitMqTopology.JobsQueue);

                // Kanal kapanana (bağlantı kopması) kadar bekle.
                var closed = new TaskCompletionSource();
                channel.ChannelShutdownAsync += (_, _) => { closed.TrySetResult(); return Task.CompletedTask; };
                await using var _ = stoppingToken.Register(() => closed.TrySetResult());
                await closed.Task;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RabbitMQ'ya bağlanılamadı; 10 sn sonra yeniden denenecek.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs ea, RabbitMqOptions o, CancellationToken ct)
    {
        SyncJobMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<SyncJobMessage>(ea.Body.Span);
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null)
        {
            // Okunamayan mesaj tekrar denenmez; kuyruk ayarı gereği ölü mektup kuyruğuna düşer.
            logger.LogError("Geçersiz mesaj ölü mektup kuyruğuna alındı.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false, ct);
            return;
        }

        var retryCount = ea.BasicProperties.Headers?.TryGetValue(RabbitMqTopology.RetryCountHeader, out var v) == true && v is int n ? n : 0;

        using var tenantScope = AmbientTenant.Use(message.TenantId);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.SyncJobs.FirstOrDefaultAsync(j => j.Id == message.JobId, ct);
        if (job is null || job.Status is SyncJobStatus.Succeeded or SyncJobStatus.Failed or SyncJobStatus.Cancelled)
        {
            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            return;
        }

        var moduleId = ModuleCatalog.ModuleFor(job.Type);
        if (!await scope.ServiceProvider.GetRequiredService<IModuleService>().IsEnabledAsync(moduleId, message.TenantId, ct))
        {
            // Modül kapalı: iş çalıştırılmaz ve yeniden denenmez; hata da sayılmaz.
            job.Status = SyncJobStatus.Cancelled;
            job.Error = $"Modül kapalı ({moduleId}); iş çalıştırılmadı.";
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("İş {JobId} ({Type}) iptal edildi: {Module} modülü kapalı.", job.Id, job.Type, moduleId);
            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            return;
        }

        var handler = scope.ServiceProvider.GetServices<ISyncJobHandler>().FirstOrDefault(h => h.Type == job.Type);
        if (handler is null)
        {
            Finish(job, SyncJobStatus.Failed, $"{job.Type} türü için işleyici tanımlı değil.");
            await db.SaveChangesAsync(ct);
            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            return;
        }

        job.Status = SyncJobStatus.Running;
        job.Attempts++;
        job.StartedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            await handler.HandleAsync(job, ct);
            Finish(job, SyncJobStatus.Succeeded, null);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "İş {JobId} ({Type}) {Attempt}. denemede hata verdi.", job.Id, job.Type, job.Attempts);
            var exhausted = job.Attempts >= o.MaxAttempts;
            Finish(job, exhausted ? SyncJobStatus.Failed : SyncJobStatus.Retrying, ex.Message);
            await db.SaveChangesAsync(ct);

            // Önce yeni mesajı yayınla, sonra ilkini onayla; araya giren çökme mesajı kaybettirmez.
            await JobQueue.PublishAsync(rabbit, exhausted ? RabbitMqTopology.DeadKey : RabbitMqTopology.RetryKey,
                message, retryCount + 1, ct);
        }

        await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
    }

    private static void Finish(SyncJob job, SyncJobStatus status, string? error)
    {
        job.Status = status;
        job.Error = error is null ? null : error.Length > 2000 ? error[..2000] : error;
        if (status is SyncJobStatus.Succeeded or SyncJobStatus.Failed) job.FinishedAt = DateTime.UtcNow;
    }
}
