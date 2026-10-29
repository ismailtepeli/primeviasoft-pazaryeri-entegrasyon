using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Infrastructure.Marketplace.Http;
using Pazaryeri.Infrastructure.Marketplace.Trendyol;

namespace Pazaryeri.Tests;

public class TrendyolClientTests
{
    private sealed record Captured(HttpMethod Method, string Url, string? Auth, string? UserAgent, string? Body);

    /// <summary>Gönderilen istekleri kaydeder ve sıradaki hazır yanıtı döner.</summary>
    private sealed class FakeServer(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        // Handler zinciri yanıtı dispose edebildiği için her çağrıda yanıtın yeni bir kopyası üretilir.
        private readonly Queue<(HttpStatusCode Code, string Body, TimeSpan? RetryAfter)> _responses = new(
            responses.Select(r => (r.StatusCode, r.Content.ReadAsStringAsync().GetAwaiter().GetResult(), r.Headers.RetryAfter?.Delta)));
        public List<Captured> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Requests.Add(new Captured(r.Method, r.RequestUri!.ToString(),
                r.Headers.Authorization?.ToString(),
                string.Join(' ', r.Headers.UserAgent.Select(p => p.ToString())) is { Length: > 0 } ua ? ua : r.Headers.TryGetValues("User-Agent", out var v) ? string.Join(' ', v) : null,
                r.Content is null ? null : await r.Content.ReadAsStringAsync(ct)));
            var (code, body, retryAfter) = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            var response = Json(code, body);
            if (retryAfter is not null) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
            return response;
        }
    }

    private sealed class RecordingLog : IApiCallLogger
    {
        public List<ApiCallEntry> Entries { get; } = [];
        public Task LogAsync(ApiCallEntry entry) { Entries.Add(entry); return Task.CompletedTask; }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MarketplaceCredentials Creds(bool staging = false) =>
        new("Trendyol", "12345", "key", "secret", "Acme", staging);

    private static (TrendyolClient Client, FakeServer Server, RecordingLog Log, List<TimeSpan> Waits) Build(
        MarketplaceCredentials creds, params HttpResponseMessage[] responses)
    {
        var server = new FakeServer(responses);
        var log = new RecordingLog();
        var waits = new List<TimeSpan>();

        // Üretimdeki sırayla: Retry (dış) -> Log (iç) -> ağ.
        var logHandler = new ApiLogHandler(log, NullLogger<ApiLogHandler>.Instance) { InnerHandler = server };
        var retry = new RetryOn429Handler { InnerHandler = logHandler, Delay = (t, _) => { waits.Add(t); return Task.CompletedTask; } };
        return (new TrendyolClient(new HttpClient(retry), creds), server, log, waits);
    }

    [Fact]
    public async Task Basic_kimlik_ve_UserAgent_basligi_gonderilir()
    {
        var (client, server, _, _) = Build(Creds(), Json(HttpStatusCode.OK, """{"batchRequestId":"b-1"}"""));

        await client.UpdateStockAndPriceAsync([new StockPriceItem("bc1", 5, 99.9m, 120m)]);

        var req = Assert.Single(server.Requests);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("key:secret")), req.Auth);
        Assert.Equal("12345 - Acme", req.UserAgent);
    }

    [Theory]
    [InlineData(false, "https://apigw.trendyol.com/")]
    [InlineData(true, "https://stageapigw.trendyol.com/")]
    public async Task Canli_ve_test_adresi_secilir(bool staging, string expectedPrefix)
    {
        var (client, server, _, _) = Build(Creds(staging), Json(HttpStatusCode.OK, """{"batchRequestId":"b-1"}"""));

        await client.SendProductsAsync([new ProductItem("bc1", "Ürün", "main", "sku", 1, 2, "açıklama", 3, 100, 90, 20, ["https://x/i.jpg"], [new ProductAttribute(10, 20)])]);

        var req = Assert.Single(server.Requests);
        Assert.StartsWith(expectedPrefix + "integration/product/sellers/12345/v2/products", req.Url);
        Assert.Contains("\"barcode\":\"bc1\"", req.Body);
        Assert.Contains("\"attributeValueIds\":[20]", req.Body);
    }

    [Fact]
    public async Task Batch_sonucu_okunur_ve_hatali_kalem_nedenleri_gelir()
    {
        const string body = """
        {"status":"COMPLETED","items":[{"requestItem":{"product":{"barcode":"bc1"}},"status":"SUCCESS","failureReasons":[]},
         {"requestItem":{"product":{"barcode":"bc2"}},"status":"FAILED","failureReasons":["Kategori hatalı"]}]}
        """;
        var (client, server, _, _) = Build(Creds(), Json(HttpStatusCode.OK, body));

        var result = await client.GetBatchResultAsync("b-1");

        Assert.EndsWith("/products/batch-requests/b-1", server.Requests[0].Url);
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal("FAILED", result.Items[1].Status);
        Assert.Equal(["Kategori hatalı"], result.Items[1].FailureReasons);
    }

    [Fact]
    public async Task Urun_ve_stok_gonderimi_batch_kimligini_dondurur()
    {
        var (client, _, _, _) = Build(Creds(), Json(HttpStatusCode.OK, """{"batchRequestId":"abc-123"}"""));

        var result = await client.UpdateStockAndPriceAsync([new StockPriceItem("bc1", 1, 10, 12)]);

        Assert.Equal("abc-123", result.BatchId);
    }

    [Fact]
    public async Task Siparisler_okunur()
    {
        const string body = """
        {"page":0,"totalPages":3,"content":[{"orderNumber":"7001","shipmentPackageId":9001,"status":"Created",
         "orderDate":1730000000000,"totalPrice":250.5,"customerFirstName":"Ayşe","customerLastName":"Yılmaz",
         "lines":[{"id":55,"barcode":"bc1","productName":"Ürün","quantity":2,"amount":125.25}]}]}
        """;
        var (client, server, _, _) = Build(Creds(), Json(HttpStatusCode.OK, body));

        var page = await client.GetOrdersAsync(new OrderQuery(Status: "Created", Page: 0, Size: 50));

        Assert.Equal(3, page.TotalPages);
        var order = Assert.Single(page.Orders);
        Assert.Equal("7001", order.OrderNumber);
        Assert.Equal("9001", order.PackageId);
        Assert.Equal("Ayşe Yılmaz", order.CustomerName);
        Assert.Equal(250.5m, order.TotalPrice);
        var line = Assert.Single(order.Lines);
        Assert.Equal("55", line.LineId);
        Assert.Equal(2, line.Quantity);
        Assert.Contains("status=Created", server.Requests[0].Url);
    }

    [Fact]
    public async Task Siparis_durumu_PUT_ile_guncellenir()
    {
        var (client, server, _, _) = Build(Creds(), Json(HttpStatusCode.OK, "{}"));

        await client.UpdateOrderStatusAsync(new OrderStatusUpdate("9001", "Invoiced", [new OrderStatusLine("55", 2)], "INV-1"));

        var req = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.EndsWith("/shipment-packages/9001", req.Url);
        Assert.Contains("\"status\":\"Invoiced\"", req.Body);
        Assert.Contains("\"invoiceNumber\":\"INV-1\"", req.Body);
    }

    [Fact]
    public async Task Yanit_429_ise_ustel_beklemeyle_yeniden_denenir()
    {
        var (client, server, _, waits) = Build(Creds(),
            Json((HttpStatusCode)429, "{}"), Json((HttpStatusCode)429, "{}"), Json((HttpStatusCode)429, "{}"),
            Json(HttpStatusCode.OK, """{"batchRequestId":"ok"}"""));

        var result = await client.UpdateStockAndPriceAsync([new StockPriceItem("bc1", 1, 10, 12)]);

        Assert.Equal("ok", result.BatchId);
        Assert.Equal(4, server.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], waits);
        // Her denemede gövde aynen yeniden gönderilmiş olmalı.
        Assert.All(server.Requests, r => Assert.Contains("\"barcode\":\"bc1\"", r.Body));
    }

    [Fact]
    public async Task Retry_After_basligi_varsa_o_sure_beklenir()
    {
        var tooMany = Json((HttpStatusCode)429, "{}");
        tooMany.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        var (client, _, _, waits) = Build(Creds(), tooMany, Json(HttpStatusCode.OK, """{"batchRequestId":"ok"}"""));

        await client.UpdateStockAndPriceAsync([new StockPriceItem("bc1", 1, 10, 12)]);

        Assert.Equal([TimeSpan.FromSeconds(7)], waits);
    }

    [Fact]
    public async Task Deneme_hakki_bitince_429_hata_olarak_firlatilir()
    {
        var (client, server, _, waits) = Build(Creds(), Json((HttpStatusCode)429, "{\"msg\":\"yavaş\"}"));

        var ex = await Assert.ThrowsAsync<MarketplaceApiException>(() =>
            client.UpdateStockAndPriceAsync([new StockPriceItem("bc1", 1, 10, 12)]));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal(6, server.Requests.Count); // ilk deneme + 5 yeniden deneme
        Assert.Equal(5, waits.Count);
    }

    [Fact]
    public async Task Basarisiz_yanit_MarketplaceApiException_olur()
    {
        var (client, _, _, _) = Build(Creds(), Json(HttpStatusCode.BadRequest, """{"errors":["barcode hatalı"]}"""));

        var ex = await Assert.ThrowsAsync<MarketplaceApiException>(() =>
            client.UpdateStockAndPriceAsync([new StockPriceItem("bc1", 1, 10, 12)]));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("barcode hatalı", ex.Body);
    }

    [Fact]
    public async Task Her_cagri_magaza_bazinda_loglanir_ve_Authorization_yazilmaz()
    {
        var (client, _, log, _) = Build(Creds(),
            Json((HttpStatusCode)429, "{}"), Json(HttpStatusCode.OK, """{"batchRequestId":"ok"}"""));

        await client.UpdateStockAndPriceAsync([new StockPriceItem("bc1", 1, 10, 12)]);

        // İç katmanda olduğu için hem 429'lu deneme hem başarılı deneme kaydedilir.
        Assert.Equal(2, log.Entries.Count);
        Assert.All(log.Entries, e =>
        {
            Assert.Equal("Trendyol", e.Channel);
            Assert.Equal("12345", e.SellerId);
            Assert.Contains("bc1", e.RequestBody);
            Assert.DoesNotContain("secret", e.RequestBody + e.ResponseBody + e.Url);
        });
        Assert.Equal([429, 200], log.Entries.Select(e => e.StatusCode));
        Assert.Contains("batchRequestId", log.Entries[1].ResponseBody);
    }
}
