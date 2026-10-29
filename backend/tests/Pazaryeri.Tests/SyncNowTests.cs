using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

/// <summary>Kuyruğa gerçekten mesaj atmadan, yalnızca SyncJob kaydı açan sahte kuyrukla çalışır.</summary>
public class SyncNowTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_SyncNow;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class DbQueue(AppDbContext db, Guid tenantId) : IJobQueue
    {
        public async Task<SyncJob> EnqueueAsync(SyncJobType type, string channel, string? batchId = null, CancellationToken ct = default)
        {
            var job = new SyncJob { TenantId = tenantId, Type = type, Channel = channel, BatchId = batchId };
            db.SyncJobs.Add(job);
            await db.SaveChangesAsync(ct);
            return job;
        }
    }

    private sealed class Handler(SyncJobType type) : ISyncJobHandler
    {
        public SyncJobType Type => type;
        public Task HandleAsync(SyncJob job, CancellationToken ct) => Task.CompletedTask;
    }

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> NewTenantAsync(string name, params (string Channel, ConnectionStatus Status)[] connections)
    {
        await using var db = NewDb(null);
        var tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        foreach (var (channel, status) in connections)
            db.ChannelConnections.Add(new ChannelConnection { TenantId = tenant.Id, Channel = channel, Status = status });
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static SyncNowController Controller(AppDbContext db, Guid tenantId, params SyncJobType[] handled) =>
        new(db, new DbQueue(db, tenantId), handled.Select(t => (ISyncJobHandler)new Handler(t)).ToList());

    [Fact]
    public async Task Bagli_her_kanal_icin_senkron_isleri_kuyruga_alinir()
    {
        var tenant = await NewTenantAsync("Sync A", ("Trendyol", ConnectionStatus.Connected), ("Hepsiburada", ConnectionStatus.AuthError));
        await using var db = NewDb(tenant);

        var result = await Controller(db, tenant, SyncJobType.OrderSync, SyncJobType.StockPriceSync).Now(default);

        Assert.Equal(new SyncNowResult(2, 0, 1), result); // kimlik hatalı kanal atlanır
        var jobs = await db.SyncJobs.ToListAsync();
        Assert.All(jobs, j => Assert.Equal("Trendyol", j.Channel));
        Assert.Equivalent(new[] { SyncJobType.OrderSync, SyncJobType.StockPriceSync }, jobs.Select(j => j.Type));
    }

    [Fact]
    public async Task Islemcisi_olmayan_tur_kuyruga_alinmaz()
    {
        var tenant = await NewTenantAsync("Sync B", ("Trendyol", ConnectionStatus.Connected));
        await using var db = NewDb(tenant);

        var result = await Controller(db, tenant, SyncJobType.OrderSync).Now(default);

        Assert.Equal(1, result.Enqueued);
        Assert.Equal(SyncJobType.OrderSync, (await db.SyncJobs.SingleAsync()).Type);
    }

    [Fact]
    public async Task Zaten_bekleyen_is_tekrar_eklenmez()
    {
        var tenant = await NewTenantAsync("Sync C", ("Trendyol", ConnectionStatus.Connected));
        await using var db = NewDb(tenant);
        var controller = Controller(db, tenant, SyncJobType.OrderSync);

        var first = await controller.Now(default);
        var second = await controller.Now(default);

        Assert.Equal(1, first.Enqueued);
        Assert.Equal(new SyncNowResult(0, 1, 1), second);
        Assert.Equal(1, await db.SyncJobs.CountAsync());
    }

    [Fact]
    public async Task Bagli_kanal_yoksa_hicbir_sey_yapilmaz()
    {
        var tenant = await NewTenantAsync("Sync D");
        await using var db = NewDb(tenant);

        var result = await Controller(db, tenant, SyncJobType.OrderSync).Now(default);

        Assert.Equal(new SyncNowResult(0, 0, 0), result);
    }
}
