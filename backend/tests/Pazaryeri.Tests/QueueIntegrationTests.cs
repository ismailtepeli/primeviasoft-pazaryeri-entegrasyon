using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Messaging;
using Pazaryeri.Infrastructure.Persistence;
using RabbitMQ.Client;

namespace Pazaryeri.Tests;

/// <summary>
/// Gerçek RabbitMQ (localhost:5672, `docker compose up -d`) ve LocalDB'ye karşı çalışır.
/// Her çalıştırmada Pazaryeri_Tests veritabanı sıfırlanır; geliştirme veritabanına dokunmaz.
/// </summary>
public class QueueIntegrationTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests;Trusted_Connection=True;TrustServerCertificate=True";

    private readonly RabbitMqOptions _options = new() { Enabled = true, MaxAttempts = 3, RetryDelaySeconds = 1 };
    private ServiceProvider _sp = null!;
    private RabbitMqConnection _rabbit = null!;
    private SyncJobWorker _worker = null!;
    private readonly FlakyHandler _flaky = new();

    private sealed class AmbientProvider : ITenantProvider
    {
        public Guid? TenantId => AmbientTenant.TenantId;
    }

    private sealed class OkHandler : ISyncJobHandler
    {
        public SyncJobType Type => SyncJobType.CatalogSync;
        public Task HandleAsync(SyncJob job, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AlwaysFailsHandler : ISyncJobHandler
    {
        public SyncJobType Type => SyncJobType.ProductSend;
        public Task HandleAsync(SyncJob job, CancellationToken ct) => throw new InvalidOperationException("kanal hatası");
    }

    private sealed class FlakyHandler : ISyncJobHandler
    {
        public SyncJobType Type => SyncJobType.OrderSync;
        public Task HandleAsync(SyncJob job, CancellationToken ct) =>
            job.Attempts == 1 ? throw new InvalidOperationException("geçici hata") : Task.CompletedTask;
    }

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(Conn));
        services.AddScoped<ITenantProvider, AmbientProvider>();
        services.AddMemoryCache(); services.AddSingleton(TimeProvider.System); services.AddScoped<Pazaryeri.Application.Modules.IModuleService, Pazaryeri.Infrastructure.Modules.ModuleService>();
        services.AddSingleton<ISyncJobHandler, OkHandler>();
        services.AddSingleton<ISyncJobHandler, AlwaysFailsHandler>();
        services.AddSingleton<ISyncJobHandler>(_flaky);
        services.AddScoped<IJobQueue, JobQueue>();
        services.AddSingleton(Options.Create(_options));
        services.AddSingleton<RabbitMqConnection>();
        _sp = services.BuildServiceProvider();

        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        _rabbit = _sp.GetRequiredService<RabbitMqConnection>();
        var connection = await _rabbit.GetAsync(CancellationToken.None);
        await using (var ch = await connection.CreateChannelAsync())
        {
            // Önceki çalıştırmadan kalan mesajları temizle.
            foreach (var q in new[] { RabbitMqTopology.JobsQueue, RabbitMqTopology.RetryQueue, RabbitMqTopology.DeadLetterQueue })
                await ch.QueuePurgeAsync(q);
        }

        _worker = new SyncJobWorker(_rabbit, _sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<SyncJobWorker>.Instance);
        await _worker.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _worker.StopAsync(CancellationToken.None);
        await _rabbit.DisposeAsync();
        await _sp.DisposeAsync();
    }

    private async Task<Guid> NewTenantAsync(string name)
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task<Guid> EnqueueAsync(Guid tenantId, SyncJobType type)
    {
        using var _ = AmbientTenant.Use(tenantId);
        await using var scope = _sp.CreateAsyncScope();
        var job = await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(type, "Trendyol", "batch-1");
        return job.Id;
    }

    private async Task<SyncJob> WaitForAsync(Guid tenantId, Guid jobId, Func<SyncJob, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            using var _ = AmbientTenant.Use(tenantId);
            await using var scope = _sp.CreateAsyncScope();
            var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SyncJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
            if (done(job)) return job;
            await Task.Delay(300);
        }
        throw new TimeoutException("İş beklenen duruma gelmedi.");
    }

    private async Task<uint> QueueCountAsync(string queue)
    {
        var connection = await _rabbit.GetAsync(CancellationToken.None);
        await using var ch = await connection.CreateChannelAsync();
        return (await ch.QueueDeclarePassiveAsync(queue)).MessageCount;
    }

    [Fact]
    public async Task Modulu_kapali_kiracinin_isi_calistirilmadan_Cancelled_olur_hata_sayilmaz()
    {
        var tenant = await NewTenantAsync("t-modul-kapali");
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TenantModules.Add(new TenantModule { TenantId = tenant, ModuleId = Pazaryeri.Application.Modules.ModuleIds.Marketplace, Enabled = false });
            await db.SaveChangesAsync();
        }
        var id = await EnqueueAsync(tenant, SyncJobType.CatalogSync); // katalog senkronu pazaryeri modülüne bağlı

        var job = await WaitForAsync(tenant, id, j => j.Status == SyncJobStatus.Cancelled);

        Assert.Equal(0, job.Attempts);
        Assert.Contains("Modül kapalı", job.Error);
        Assert.Equal(0u, await QueueCountAsync(RabbitMqTopology.DeadLetterQueue));
    }

    [Fact]
    public async Task Basarili_is_tek_denemede_Succeeded_olur()
    {
        var tenant = await NewTenantAsync("t-ok");
        var id = await EnqueueAsync(tenant, SyncJobType.CatalogSync);

        var job = await WaitForAsync(tenant, id, j => j.Status == SyncJobStatus.Succeeded);

        Assert.Equal(1, job.Attempts);
        Assert.Null(job.Error);
        Assert.NotNull(job.FinishedAt);
    }

    [Fact]
    public async Task Gecici_hata_retry_kuyrugundan_gecip_ikinci_denemede_basarir()
    {
        var tenant = await NewTenantAsync("t-flaky");
        var id = await EnqueueAsync(tenant, SyncJobType.OrderSync);

        var job = await WaitForAsync(tenant, id, j => j.Status == SyncJobStatus.Succeeded);

        Assert.Equal(2, job.Attempts);
    }

    [Fact]
    public async Task Surekli_hata_deneme_hakki_bitince_Failed_olur_ve_olu_mektup_kuyruguna_duser()
    {
        var tenant = await NewTenantAsync("t-fail");
        var id = await EnqueueAsync(tenant, SyncJobType.ProductSend);

        var job = await WaitForAsync(tenant, id, j => j.Status == SyncJobStatus.Failed);

        Assert.Equal(_options.MaxAttempts, job.Attempts);
        Assert.Equal("kanal hatası", job.Error);
        Assert.Equal(1u, await QueueCountAsync(RabbitMqTopology.DeadLetterQueue));
    }

    [Fact]
    public async Task Isleyicisi_olmayan_tur_hemen_Failed_olur()
    {
        var tenant = await NewTenantAsync("t-nohandler");
        var id = await EnqueueAsync(tenant, SyncJobType.InvoiceSend);

        var job = await WaitForAsync(tenant, id, j => j.Status == SyncJobStatus.Failed);

        Assert.Equal(0, job.Attempts);
        Assert.Contains("işleyici", job.Error);
    }

    [Fact]
    public async Task Kiracı_baska_kiracinin_isini_goremez()
    {
        var a = await NewTenantAsync("t-a");
        var b = await NewTenantAsync("t-b");
        var jobA = await EnqueueAsync(a, SyncJobType.CatalogSync);
        await WaitForAsync(a, jobA, j => j.Status == SyncJobStatus.Succeeded);

        using (AmbientTenant.Use(b))
        {
            await using var scope = _sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.SyncJobs.ToListAsync());
            Assert.Single(await db.SyncJobs.IgnoreQueryFilters().Where(j => j.Id == jobA).ToListAsync());
        }
    }
}
