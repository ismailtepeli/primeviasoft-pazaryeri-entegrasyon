using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Infrastructure.Marketplace.Http;
using Pazaryeri.Infrastructure.Marketplace.Trendyol;
using Pazaryeri.Sandbox;

namespace Pazaryeri.Tests;

/// <summary>
/// TrendyolClient'ı gerçek HTTP üzerinden yerel sandbox'a (tools/Pazaryeri.Sandbox) karşı sınar.
/// Sandbox dokümana göre bağımsız yazıldığı için istemcinin sözleşmeyi yanlış anlaması burada yakalanır.
/// Gerçek Trendyol ile ilk temasta ortaya çıkan farklar sandbox'a eklenip buraya test olarak yazılmalıdır.
/// </summary>
public class TrendyolSandboxTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private string _baseUrl = null!;
    private readonly HttpClient _admin = new();

    public async Task InitializeAsync()
    {
        _app = SandboxApp.Build(["--urls", "http://127.0.0.1:0"]);
        await _app.StartAsync();
        _baseUrl = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        await _app.DisposeAsync();
    }

    private static MarketplaceCredentials Creds(string key = "key", string secret = "secret") =>
        new("Trendyol", "12345", key, secret, "Acme");

    private TrendyolClient Client(HttpMessageHandler? handler = null, MarketplaceCredentials? creds = null) =>
        new(handler is null ? new HttpClient() : new HttpClient(handler), creds ?? Creds(), _baseUrl);

    private static ProductItem Product(string barcode = "BC-1", decimal list = 120m, decimal sale = 100m, long categoryId = 11, bool withAttributes = true) =>
        new(barcode, "Test tişört", "MAIN-1", "STK-1", 1, categoryId, "Açıklama", 10, list, sale, 20,
            ["https://img.example/1.jpg"],
            withAttributes ? [new ProductAttribute(338, 6980), new ProductAttribute(47, 192)] : []);

    private async Task SeedOrdersAsync(int count, string status = "Created", string barcode = "BC-1")
    {
        using var r = await _admin.PostAsJsonAsync($"{_baseUrl}/__sandbox/orders", new { count, status, barcode });
        r.EnsureSuccessStatusCode();
    }

    private Task AdminAsync(string path, object body) => _admin.PostAsJsonAsync($"{_baseUrl}{path}", body);

    [Fact]
    public async Task Baglanti_testi_gecerli_kimlikle_basarili_olur()
    {
        await Client().TestConnectionAsync();
    }

    [Fact]
    public async Task Bos_kimlik_401_ile_reddedilir()
    {
        var ex = await Assert.ThrowsAsync<MarketplaceApiException>(() => Client(creds: Creds("", "")).TestConnectionAsync());
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task Katalog_kategori_marka_ve_ozellikleri_okunur()
    {
        var client = Client();

        var categories = await client.GetCategoriesAsync();
        Assert.Contains(categories, c => c.Id == 11 && c.ParentId == 1);

        var pages = new List<int>();
        await foreach (var page in client.GetBrandPagesAsync()) pages.Add(page.Count);
        Assert.Equal([1000, 1000, 500], pages);

        var attrs = await client.GetCategoryAttributesAsync(11);
        Assert.Contains(attrs, a => a.Id == 338 && a.Required);
        Assert.Equal(["S", "M", "L"], (await client.GetAttributeValuesAsync(11, 338)).Select(v => v.Name));
        Assert.Contains(attrs, a => a.Id == 910 && a.AllowCustom);
    }

    [Fact]
    public async Task Gecerli_urun_gonderimi_batch_sonucunda_basarili_gorunur()
    {
        var client = Client();

        var batch = await client.SendProductsAsync([Product()]);
        var status = await client.GetBatchResultAsync(batch.BatchId);

        Assert.Equal("COMPLETED", status.Status);
        var item = Assert.Single(status.Items);
        Assert.Equal("BC-1", item.Barcode);
        Assert.Equal("SUCCESS", item.Status);
    }

    [Fact]
    public async Task Gecersiz_urun_nedenleriyle_FAILED_doner()
    {
        var client = Client();

        var batch = await client.SendProductsAsync([Product(list: 50m, sale: 100m, withAttributes: false)]);
        var item = Assert.Single((await client.GetBatchResultAsync(batch.BatchId)).Items);

        Assert.Equal("FAILED", item.Status);
        Assert.Contains(item.FailureReasons, r => r.Contains("liste fiyatından"));
        Assert.Contains(item.FailureReasons, r => r.Contains("Beden"));
        Assert.Contains(item.FailureReasons, r => r.Contains("Renk"));
    }

    [Fact]
    public async Task Ust_kategorisi_olan_kategoriye_urun_gonderilemez()
    {
        var client = Client();

        var batch = await client.SendProductsAsync([Product(categoryId: 1)]);
        var item = Assert.Single((await client.GetBatchResultAsync(batch.BatchId)).Items);

        Assert.Equal("FAILED", item.Status);
        Assert.Contains(item.FailureReasons, r => r.Contains("Kategori"));
    }

    [Fact]
    public async Task Stok_fiyat_guncellemesi_kayitli_barkodu_gunceller_bilinmeyeni_reddeder()
    {
        var client = Client();
        await client.SendProductsAsync([Product()]);

        var batch = await client.UpdateStockAndPriceAsync(
            [new StockPriceItem("BC-1", 5, 90m, 120m), new StockPriceItem("YOK", 5, 90m, 120m)]);
        var items = (await client.GetBatchResultAsync(batch.BatchId)).Items;

        Assert.Equal("SUCCESS", items.Single(i => i.Barcode == "BC-1").Status);
        var unknown = items.Single(i => i.Barcode == "YOK");
        Assert.Equal("FAILED", unknown.Status);
        Assert.Contains("Barkod bulunamadı.", unknown.FailureReasons);

        var state = await _admin.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>($"{_baseUrl}/__sandbox/state");
        var product = state!["products"]!.AsArray().Single()!;
        Assert.Equal(90m, product["salePrice"]!.GetValue<decimal>());
        Assert.Equal(5, product["quantity"]!.GetValue<int>());
    }

    [Fact]
    public async Task Stok_fiyat_kurallari_ihlalinde_kalem_reddedilir()
    {
        var client = Client();
        await client.SendProductsAsync([Product()]);

        var batch = await client.UpdateStockAndPriceAsync([new StockPriceItem("BC-1", 20001, 200m, 120m)]);
        var item = Assert.Single((await client.GetBatchResultAsync(batch.BatchId)).Items);

        Assert.Equal("FAILED", item.Status);
        Assert.Equal(2, item.FailureReasons.Count);
    }

    [Fact]
    public async Task Bekleyen_batch_once_IN_PROGRESS_sonra_tamamlanir()
    {
        var client = Client();
        await AdminAsync("/__sandbox/pending-polls", new { count = 2 });
        var batch = await client.SendProductsAsync([Product()]);

        Assert.Equal("IN_PROGRESS", (await client.GetBatchResultAsync(batch.BatchId)).Status);
        Assert.Equal("IN_PROGRESS", (await client.GetBatchResultAsync(batch.BatchId)).Status);
        Assert.Equal("COMPLETED", (await client.GetBatchResultAsync(batch.BatchId)).Status);
    }

    [Fact]
    public async Task Siparisler_sayfalanir_ve_durumla_suzulur()
    {
        await SeedOrdersAsync(5);
        await SeedOrdersAsync(2, status: "Shipped");
        var client = Client();

        var first = await client.GetOrdersAsync(new OrderQuery(Size: 3));
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(3, first.Orders.Count);
        var order = first.Orders[0];
        Assert.StartsWith("SBX", order.OrderNumber);
        Assert.Equal("BC-1", order.Lines[0].Barcode);
        Assert.True(order.TotalPrice > 0);
        Assert.NotNull(order.LastModified);

        var shipped = await client.GetOrdersAsync(new OrderQuery(Status: "Shipped"));
        Assert.Equal(2, shipped.Orders.Count);
        Assert.All(shipped.Orders, o => Assert.Equal("Shipped", o.Status));
    }

    [Fact]
    public async Task Artimli_senkron_yalnizca_sonradan_degisen_paketleri_getirir()
    {
        await SeedOrdersAsync(2);
        var client = Client();
        var before = DateTime.UtcNow;
        await Task.Delay(20);
        var target = (await client.GetOrdersAsync(new OrderQuery())).Orders[0];

        await client.UpdateOrderStatusAsync(new OrderStatusUpdate(target.PackageId, "Picking", [new OrderStatusLine(target.Lines[0].LineId, 1)]));

        var changed = await client.GetOrdersAsync(new OrderQuery(ModifiedFrom: before));
        var one = Assert.Single(changed.Orders);
        Assert.Equal(target.PackageId, one.PackageId);
        Assert.Equal("Picking", one.Status);
    }

    [Fact]
    public async Task Siparis_durumu_gecerli_sirayla_ilerler_gecersiz_gecis_reddedilir()
    {
        await SeedOrdersAsync(1);
        var client = Client();
        var order = (await client.GetOrdersAsync(new OrderQuery())).Orders.Single();
        var lines = new[] { new OrderStatusLine(order.Lines[0].LineId, order.Lines[0].Quantity) };

        var skip = await Assert.ThrowsAsync<MarketplaceApiException>(() =>
            client.UpdateOrderStatusAsync(new OrderStatusUpdate(order.PackageId, "Shipped", lines)));
        Assert.Equal(400, skip.StatusCode);

        await client.UpdateOrderStatusAsync(new OrderStatusUpdate(order.PackageId, "Picking", lines));

        var noInvoice = await Assert.ThrowsAsync<MarketplaceApiException>(() =>
            client.UpdateOrderStatusAsync(new OrderStatusUpdate(order.PackageId, "Invoiced", lines)));
        Assert.Contains("invoiceNumber", noInvoice.Body);

        await client.UpdateOrderStatusAsync(new OrderStatusUpdate(order.PackageId, "Invoiced", lines, InvoiceNumber: "FTR-1"));
        Assert.Equal("Invoiced", (await client.GetOrdersAsync(new OrderQuery())).Orders.Single().Status);
    }

    [Fact]
    public async Task Bilinmeyen_paket_404_doner()
    {
        var ex = await Assert.ThrowsAsync<MarketplaceApiException>(() =>
            Client().UpdateOrderStatusAsync(new OrderStatusUpdate("999", "Picking", [new OrderStatusLine("1", 1)])));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Webhook_kaydi_https_ister_gecerli_kayit_kimlik_doner()
    {
        var client = Client();

        var http = await Assert.ThrowsAsync<MarketplaceApiException>(() =>
            client.CreateWebhookAsync(new WebhookRegistration("http://example.com/hook", "u", "p", ["Created"])));
        Assert.Equal(400, http.StatusCode);

        var ok = await client.CreateWebhookAsync(new WebhookRegistration("https://example.com/hook", "u", "p", ["Created"]));
        Assert.False(string.IsNullOrEmpty(ok.WebhookId));
    }

    [Fact]
    public async Task Enjekte_edilen_429_yeniden_denemeyle_asilir()
    {
        await AdminAsync("/__sandbox/fail", new { count = 2, status = 429, retryAfterSeconds = 1 });
        var waits = new List<TimeSpan>();
        var retry = new RetryOn429Handler
        {
            InnerHandler = new HttpClientHandler(),
            Delay = (t, _) => { waits.Add(t); return Task.CompletedTask; },
        };

        await Client(retry).TestConnectionAsync();

        Assert.Equal(2, waits.Count);
    }

    [Fact]
    public async Task Enjekte_edilen_500_istemcide_MarketplaceApiException_olur()
    {
        await AdminAsync("/__sandbox/fail", new { count = 1, status = 500 });

        var ex = await Assert.ThrowsAsync<MarketplaceApiException>(() => Client().TestConnectionAsync());
        Assert.Equal(500, ex.StatusCode);

        await Client().TestConnectionAsync(); // hata hakkı bitti, sonraki istek geçer
    }
}
