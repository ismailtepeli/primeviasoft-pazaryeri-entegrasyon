using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

/// <summary>LocalDB'de ayrı bir veritabanı (Pazaryeri_Tests_Dashboard) kullanır; kuyruk testleriyle çakışmaz.</summary>
public class DashboardChannelsTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Dashboard;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
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

    private static async Task<Guid> NewTenantAsync(string name)
    {
        await using var db = NewDb(null);
        var tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    [Fact]
    public async Task Kaydi_olmayan_kanal_baglanmadi_gorunur()
    {
        var tenant = await NewTenantAsync("Bos Firma");
        await using var db = NewDb(tenant);

        var result = await new DashboardController(db).Channels(default);

        Assert.Equal(["Trendyol", "Hepsiburada", "Amazon"], result.Select(c => c.Channel));
        Assert.All(result, c =>
        {
            Assert.Equal("NotConnected", c.Status);
            Assert.Null(c.LastSyncAt);
        });
    }

    [Fact]
    public async Task Baglanti_durumu_ve_son_basarili_senkron_zamani_gelir()
    {
        var tenant = await NewTenantAsync("Dolu Firma");
        var older = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
        var newer = older.AddHours(2);

        await using (var seed = NewDb(tenant))
        {
            seed.ChannelConnections.Add(new ChannelConnection { Channel = "Trendyol", Status = ConnectionStatus.Connected });
            seed.ChannelConnections.Add(new ChannelConnection { Channel = "Hepsiburada", Status = ConnectionStatus.AuthError });
            seed.SyncJobs.AddRange(
                new SyncJob { Channel = "Trendyol", Type = SyncJobType.OrderSync, Status = SyncJobStatus.Succeeded, FinishedAt = older },
                new SyncJob { Channel = "Trendyol", Type = SyncJobType.OrderSync, Status = SyncJobStatus.Succeeded, FinishedAt = newer },
                new SyncJob { Channel = "Trendyol", Type = SyncJobType.OrderSync, Status = SyncJobStatus.Failed, FinishedAt = newer.AddHours(1) });
            await seed.SaveChangesAsync();
        }

        await using var db = NewDb(tenant);
        var result = await new DashboardController(db).Channels(default);

        var trendyol = result.Single(c => c.Channel == "Trendyol");
        Assert.Equal("Connected", trendyol.Status);
        Assert.Equal(newer, trendyol.LastSyncAt); // başarısız iş son senkron sayılmaz
        Assert.Equal("AuthError", result.Single(c => c.Channel == "Hepsiburada").Status);
        Assert.Equal("NotConnected", result.Single(c => c.Channel == "Amazon").Status);
    }

    [Fact]
    public async Task Senkron_hatalari_yeniden_eskiye_ve_ilgili_ekranla_gelir()
    {
        var tenant = await NewTenantAsync("Hatali Firma");
        var t0 = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
        await using (var seed = NewDb(tenant))
        {
            seed.SyncJobs.AddRange(
                new SyncJob { Channel = "Trendyol", Type = SyncJobType.ProductSend, Status = SyncJobStatus.Failed, Error = "Kategori hatalı", AffectedCount = 12, FinishedAt = t0 },
                new SyncJob { Channel = "Hepsiburada", Type = SyncJobType.OrderSync, Status = SyncJobStatus.Failed, Error = "401", FinishedAt = t0.AddHours(1) },
                new SyncJob { Channel = "Trendyol", Type = SyncJobType.OrderSync, Status = SyncJobStatus.Succeeded, FinishedAt = t0.AddHours(2) });
            await seed.SaveChangesAsync();
        }

        await using var db = NewDb(tenant);
        var page = await new DashboardController(db).SyncErrors(0, 5, default);

        Assert.Equal(2, page.Total); // başarılı iş hata sayılmaz
        Assert.Equal(["Hepsiburada", "Trendyol"], page.Items.Select(i => i.Channel));
        var product = page.Items[1];
        Assert.Equal("Kategori hatalı", product.Message);
        Assert.Equal(12, product.AffectedCount);
        Assert.Equal("/urunler", product.Target);
        Assert.Equal("/siparisler", page.Items[0].Target);
    }

    [Fact]
    public async Task Senkron_hatalari_sayfalanir_ve_baska_kiracininki_gorunmez()
    {
        var a = await NewTenantAsync("Sayfa A");
        var b = await NewTenantAsync("Sayfa B");
        await using (var seed = NewDb(a))
        {
            for (var i = 0; i < 7; i++)
                seed.SyncJobs.Add(new SyncJob { Channel = "Trendyol", Type = SyncJobType.StockPriceSync, Status = SyncJobStatus.Failed, Error = $"e{i}", FinishedAt = DateTime.UtcNow.AddMinutes(-i) });
            await seed.SaveChangesAsync();
        }

        await using var dbA = NewDb(a);
        var first = await new DashboardController(dbA).SyncErrors(0, 5, default);
        var second = await new DashboardController(dbA).SyncErrors(5, 5, default);
        await using var dbB = NewDb(b);

        Assert.Equal(7, first.Total);
        Assert.Equal(5, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(0, (await new DashboardController(dbB).SyncErrors(0, 5, default)).Total);
    }

    [Fact]
    public async Task Baska_kiracinin_baglantisi_gorunmez()
    {
        var a = await NewTenantAsync("A Firma");
        var b = await NewTenantAsync("B Firma");
        await using (var seed = NewDb(a))
        {
            seed.ChannelConnections.Add(new ChannelConnection { Channel = "Trendyol" });
            await seed.SaveChangesAsync();
        }

        await using var db = NewDb(b);
        var result = await new DashboardController(db).Channels(default);

        Assert.Equal("NotConnected", result.Single(c => c.Channel == "Trendyol").Status);
    }
}
