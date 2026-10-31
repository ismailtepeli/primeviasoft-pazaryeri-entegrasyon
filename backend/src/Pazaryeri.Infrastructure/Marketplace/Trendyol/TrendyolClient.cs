using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Pazaryeri.Application.Catalog;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Infrastructure.Marketplace.Http;

namespace Pazaryeri.Infrastructure.Marketplace.Trendyol;

/// <summary>
/// Trendyol entegrasyon API adapter'ı. Kimlik: Basic (apiKey:apiSecret);
/// User-Agent: "{sellerId} - {Firma}". Uç nokta yolları Trendyol dokümanına göredir.
/// </summary>
public class TrendyolClient(HttpClient http, MarketplaceCredentials credentials) : IMarketplaceClient, IChannelCatalogClient
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
        using var doc = await SendAsync(HttpMethod.Post, $"/integration/product/sellers/{Seller}/v2/products", new { items }, TrendyolServiceGroup.ProductWrite, ct);
        return ReadBatch(doc);
    }

    public async Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> items, CancellationToken ct = default)
    {
        var body = new
        {
            items = items.Select(i => new { barcode = i.Barcode, quantity = i.Quantity, salePrice = i.SalePrice, listPrice = i.ListPrice }),
        };
        using var doc = await SendAsync(HttpMethod.Post, $"/integration/inventory/sellers/{Seller}/products/price-and-inventory", body, TrendyolServiceGroup.InventoryPriceWrite, ct);
        return ReadBatch(doc);
    }

    /// <summary>Marka listesinin ilk sayfası: kimlik, User-Agent ve ortam doğruysa hafif ve zararsız bir okuma.</summary>
    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Get, "/integration/product/brands?page=0&size=1", null, TrendyolServiceGroup.ProductRead, ct);
    }

    public async Task<BatchStatus> GetBatchResultAsync(string batchId, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/integration/product/sellers/{Seller}/products/batch-requests/{Uri.EscapeDataString(batchId)}", null, TrendyolServiceGroup.ProductRead, ct);
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
        if (query.ModifiedFrom is not null) qs.Add($"lastModifiedStartDate={ToEpochMs(query.ModifiedFrom.Value)}");

        using var doc = await SendAsync(HttpMethod.Get, $"/integration/order/sellers/{Seller}/orders?{string.Join('&', qs)}", null, TrendyolServiceGroup.Other, ct);
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
                    lines,
                    o.TryGetProperty("lastModifiedDate", out var lm) && lm.ValueKind == JsonValueKind.Number
                        ? DateTimeOffset.FromUnixTimeMilliseconds(lm.GetInt64()).UtcDateTime : null));
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
        using var _ = await SendAsync(HttpMethod.Put, $"/integration/order/sellers/{Seller}/shipment-packages/{packageId}", body, TrendyolServiceGroup.Other, ct);
    }

    /// <summary>
    /// Webhook kaydı. Uç nokta ve gövde Trendyol belgesine göre yazıldı; gerçek API ile henüz doğrulanmadı
    /// (gerçek satıcı hesabı gerekir), sahte HTTP işleyicisiyle test edilir.
    /// </summary>
    public async Task<WebhookRegistrationResult> CreateWebhookAsync(WebhookRegistration registration, CancellationToken ct = default)
    {
        var body = new
        {
            url = registration.Url,
            authenticationType = "BASIC_AUTHENTICATION",
            username = registration.Username,
            password = registration.Password,
            subscribedStatuses = registration.Statuses,
        };
        using var doc = await SendAsync(HttpMethod.Post, $"/integration/webhook/sellers/{Seller}/webhooks", body, TrendyolServiceGroup.Other, ct);
        return new WebhookRegistrationResult(doc.RootElement.ValueKind == JsonValueKind.Object ? Str(doc.RootElement, "id").NullIfEmpty() : null);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, TrendyolServiceGroup group, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.ApiKey}:{credentials.ApiSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.TryAddWithoutValidation("User-Agent", $"{credentials.SellerId} - {credentials.CompanyName}");
        request.Options.Set(MarketplaceRequestOptions.Channel, ChannelName);
        request.Options.Set(MarketplaceRequestOptions.SellerId, credentials.SellerId);
        request.Options.Set(MarketplaceRequestOptions.RateLimits, TrendyolRateLimits.RulesFor(credentials.Tier, group, method, path));
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new MarketplaceApiException(ChannelName, (int)response.StatusCode, text);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    // --- katalog (kategori, marka, özellik) ---

    public const int BrandPageSize = 1000;
    /// <summary>Sonsuz döngüye karşı üst sınır; 1.000'lik sayfalarla 500.000 marka.</summary>
    public const int MaxBrandPages = 500;

    public async Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, "/integration/product/product-categories", null, TrendyolServiceGroup.ProductRead, ct);
        var result = new List<CatalogCategory>();
        if (doc.RootElement.TryGetProperty("categories", out var roots)) Flatten(roots, null, result);
        return result;
    }

    private static void Flatten(JsonElement nodes, long? parent, List<CatalogCategory> into)
    {
        foreach (var n in nodes.EnumerateArray())
        {
            if (!n.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;
            var id = idEl.GetInt64();
            var parentId = n.TryGetProperty("parentId", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : parent;
            into.Add(new CatalogCategory(id, parentId, Str(n, "name")));
            if (n.TryGetProperty("subCategories", out var subs) && subs.ValueKind == JsonValueKind.Array) Flatten(subs, id, into);
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<CatalogBrand>> GetBrandPagesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var page = 0; page < MaxBrandPages; page++)
        {
            using var doc = await SendAsync(HttpMethod.Get, $"/integration/product/brands?page={page}&size={BrandPageSize}", null, TrendyolServiceGroup.ProductRead, ct);
            var brands = new List<CatalogBrand>();
            if (doc.RootElement.TryGetProperty("brands", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var b in arr.EnumerateArray())
                    if (b.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number) brands.Add(new CatalogBrand(id.GetInt64(), Str(b, "name")));
            if (brands.Count == 0) yield break;
            yield return brands;
            if (brands.Count < BrandPageSize) yield break;
        }
    }

    public async Task<IReadOnlyList<CatalogAttribute>> GetCategoryAttributesAsync(long categoryId, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/integration/product/categories/{categoryId}/attributes", null, TrendyolServiceGroup.ProductRead, ct);
        var result = new List<CatalogAttribute>();
        if (!doc.RootElement.TryGetProperty("categoryAttributes", out var arr) || arr.ValueKind != JsonValueKind.Array) return result;
        foreach (var a in arr.EnumerateArray())
        {
            if (!a.TryGetProperty("attribute", out var attr) || !attr.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;
            var values = a.TryGetProperty("attributeValues", out var vs) && vs.ValueKind == JsonValueKind.Array ? ReadValues(vs) : [];
            result.Add(new CatalogAttribute(idEl.GetInt64(), Str(attr, "name"), Bool(a, "required"), Bool(a, "allowCustom"), Bool(a, "varianter"), Bool(a, "slicer"), values));
        }
        return result;
    }

    public async Task<IReadOnlyList<CatalogValue>> GetAttributeValuesAsync(long categoryId, long attributeId, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/integration/product/categories/{categoryId}/attributes/{attributeId}/values", null, TrendyolServiceGroup.ProductRead, ct);
        var root = doc.RootElement;
        // Yanıt şekli sürümler arasında değişebildiği için dizi, "attributeValues" ya da "values" olarak okunur.
        if (root.ValueKind == JsonValueKind.Array) return ReadValues(root);
        foreach (var name in new[] { "attributeValues", "values", "content" })
            if (root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array) return ReadValues(arr);
        return [];
    }

    private static List<CatalogValue> ReadValues(JsonElement arr) =>
        arr.EnumerateArray()
            .Where(v => v.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
            .Select(v => new CatalogValue(v.GetProperty("id").GetInt64(), Str(v, "name")))
            .ToList();

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

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

internal static class TrendyolStringExtensions
{
    public static string? NullIfEmpty(this string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
