using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Infrastructure.Marketplace.Http;

namespace Pazaryeri.Infrastructure.Marketplace.Trendyol;

/// <summary>
/// Trendyol entegrasyon API adapter'ı. Kimlik: Basic (apiKey:apiSecret);
/// User-Agent: "{sellerId} - {Firma}". Uç nokta yolları Trendyol dokümanına göredir.
/// </summary>
public class TrendyolClient(HttpClient http, MarketplaceCredentials credentials) : IMarketplaceClient
{
    public const string ChannelName = "Trendyol";
    public const string ProductionBaseUrl = "https://apigw.trendyol.com";
    public const string StagingBaseUrl = "https://stageapigw.trendyol.com";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Channel => ChannelName;

    private string BaseUrl => credentials.UseStaging ? StagingBaseUrl : ProductionBaseUrl;
    private string Seller => Uri.EscapeDataString(credentials.SellerId);

    public async Task<BatchResult> SendProductsAsync(IReadOnlyList<ProductItem> products, CancellationToken ct = default)
    {
        var items = products.Select(p => new
        {
            barcode = p.Barcode,
            title = p.Title,
            productMainId = p.ProductMainId,
            stockCode = p.StockCode,
            brandId = p.BrandId,
            categoryId = p.CategoryId,
            description = p.Description,
            quantity = p.Quantity,
            listPrice = p.ListPrice,
            salePrice = p.SalePrice,
            vatRate = p.VatRate,
            currencyType = p.CurrencyType,
            cargoCompanyId = p.CargoCompanyId,
            dimensionalWeight = p.DimensionalWeight,
            images = p.ImageUrls.Select(u => new { url = u }),
            attributes = p.Attributes.Select(a => new
            {
                attributeId = a.AttributeId,
                attributeValueIds = a.ValueId is null ? null : new[] { a.ValueId.Value },
                attributeValue = a.CustomValue,
            }),
        });
        using var doc = await SendAsync(HttpMethod.Post, $"/integration/product/sellers/{Seller}/v2/products", new { items }, ct);
        return ReadBatch(doc);
    }

    public async Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> items, CancellationToken ct = default)
    {
        var body = new
        {
            items = items.Select(i => new { barcode = i.Barcode, quantity = i.Quantity, salePrice = i.SalePrice, listPrice = i.ListPrice }),
        };
        using var doc = await SendAsync(HttpMethod.Post, $"/integration/inventory/sellers/{Seller}/products/price-and-inventory", body, ct);
        return ReadBatch(doc);
    }

    public async Task<BatchStatus> GetBatchResultAsync(string batchId, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/integration/product/sellers/{Seller}/products/batch-requests/{Uri.EscapeDataString(batchId)}", null, ct);
        var root = doc.RootElement;
        var items = new List<BatchItemResult>();
        if (root.TryGetProperty("items", out var arr))
        {
            foreach (var i in arr.EnumerateArray())
            {
                var barcode = i.TryGetProperty("requestItem", out var ri) && ri.TryGetProperty("product", out var pr) ? Str(pr, "barcode")
                    : i.TryGetProperty("requestItem", out ri) ? Str(ri, "barcode") : Str(i, "barcode");
                var reasons = i.TryGetProperty("failureReasons", out var fr) && fr.ValueKind == JsonValueKind.Array
                    ? fr.EnumerateArray().Select(r => r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : r.ToString()).ToList()
                    : [];
                items.Add(new BatchItemResult(barcode, Str(i, "status"), reasons));
            }
        }
        return new BatchStatus(batchId, Str(root, "status"), items);
    }

    public async Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken ct = default)
    {
        var qs = new List<string> { $"page={query.Page}", $"size={query.Size}", "orderByField=PackageLastModifiedDate", "orderByDirection=DESC" };
        if (query.Status is not null) qs.Add($"status={Uri.EscapeDataString(query.Status)}");
        if (query.From is not null) qs.Add($"startDate={ToEpochMs(query.From.Value)}");
        if (query.To is not null) qs.Add($"endDate={ToEpochMs(query.To.Value)}");

        using var doc = await SendAsync(HttpMethod.Get, $"/integration/order/sellers/{Seller}/orders?{string.Join('&', qs)}", null, ct);
        var root = doc.RootElement;

        var orders = new List<MarketplaceOrder>();
        if (root.TryGetProperty("content", out var content))
        {
            foreach (var o in content.EnumerateArray())
            {
                var lines = o.TryGetProperty("lines", out var ls)
                    ? ls.EnumerateArray().Select(l => new MarketplaceOrderLine(
                        Str(l, "id"), Str(l, "barcode"), Str(l, "productName"),
                        Int(l, "quantity"), Dec(l, "amount"))).ToList()
                    : [];
                orders.Add(new MarketplaceOrder(
                    Str(o, "orderNumber"),
                    Str(o, "shipmentPackageId", fallback: Str(o, "id")),
                    Str(o, "status"),
                    DateTimeOffset.FromUnixTimeMilliseconds(o.TryGetProperty("orderDate", out var d) ? d.GetInt64() : 0).UtcDateTime,
                    Dec(o, "totalPrice", fallback: Dec(o, "grossAmount")),
                    $"{Str(o, "customerFirstName")} {Str(o, "customerLastName")}".Trim(),
                    lines));
            }
        }
        return new OrderPage(orders, Int(root, "page"), Int(root, "totalPages"));
    }

    public async Task UpdateOrderStatusAsync(OrderStatusUpdate update, CancellationToken ct = default)
    {
        var body = new
        {
            lines = update.Lines.Select(l => new { lineId = long.Parse(l.LineId, CultureInfo.InvariantCulture), quantity = l.Quantity }),
            @params = update.InvoiceNumber is null ? new Dictionary<string, string>() : new() { ["invoiceNumber"] = update.InvoiceNumber },
            status = update.Status,
        };
        var packageId = Uri.EscapeDataString(update.PackageId);
        using var _ = await SendAsync(HttpMethod.Put, $"/integration/order/sellers/{Seller}/shipment-packages/{packageId}", body, ct);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.ApiKey}:{credentials.ApiSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.TryAddWithoutValidation("User-Agent", $"{credentials.SellerId} - {credentials.CompanyName}");
        request.Options.Set(MarketplaceRequestOptions.Channel, ChannelName);
        request.Options.Set(MarketplaceRequestOptions.SellerId, credentials.SellerId);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new MarketplaceApiException(ChannelName, (int)response.StatusCode, text);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static BatchResult ReadBatch(JsonDocument doc) =>
        new(doc.RootElement.TryGetProperty("batchRequestId", out var id) ? id.GetString() ?? "" : "");

    private static long ToEpochMs(DateTime t) => new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private static string Str(JsonElement e, string name, string fallback = "") =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : v.ToString()
            : fallback;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static decimal Dec(JsonElement e, string name, decimal fallback = 0) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : fallback;
}
