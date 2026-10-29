namespace Pazaryeri.Application.Marketplace;

/// <summary>Bir pazaryeri kanalına bağlanmak için gereken bilgiler (mağaza ayarlarından gelir).</summary>
public record MarketplaceCredentials(
    string Channel,
    string SellerId,
    string ApiKey,
    string ApiSecret,
    string CompanyName,
    bool UseStaging = false);

public record ProductAttribute(long AttributeId, long? ValueId = null, string? CustomValue = null);

public record ProductItem(
    string Barcode,
    string Title,
    string ProductMainId,
    string StockCode,
    long BrandId,
    long CategoryId,
    string Description,
    int Quantity,
    decimal ListPrice,
    decimal SalePrice,
    int VatRate,
    IReadOnlyList<string> ImageUrls,
    IReadOnlyList<ProductAttribute> Attributes,
    long? CargoCompanyId = null,
    decimal? DimensionalWeight = null,
    string CurrencyType = "TRY");

/// <summary>Toplu işlemdeki tek kalemin sonucu; hatalıysa nedenleri FailureReasons'tadır.</summary>
public record BatchItemResult(string Barcode, string Status, IReadOnlyList<string> FailureReasons);

/// <summary>Toplu işlemin sonucu. Stok-fiyat batch'lerinde genel Status boş gelebilir; kalemlere bakılır.</summary>
public record BatchStatus(string BatchId, string Status, IReadOnlyList<BatchItemResult> Items);

public record StockPriceItem(string Barcode, int Quantity, decimal SalePrice, decimal ListPrice);

/// <summary>Kanalın toplu işlem (batch) kimliği; sonucu sonradan bu kimlikle sorgulanır.</summary>
public record BatchResult(string BatchId);

public record OrderQuery(DateTime? From = null, DateTime? To = null, string? Status = null, int Page = 0, int Size = 50);

public record MarketplaceOrderLine(string LineId, string Barcode, string Name, int Quantity, decimal Price);

public record MarketplaceOrder(
    string OrderNumber,
    string PackageId,
    string Status,
    DateTime OrderDate,
    decimal TotalPrice,
    string CustomerName,
    IReadOnlyList<MarketplaceOrderLine> Lines);

public record OrderPage(IReadOnlyList<MarketplaceOrder> Orders, int Page, int TotalPages);

/// <summary>Sipariş paketinin durumunu değiştirir (örn. Picking, Invoiced).</summary>
public record OrderStatusUpdate(string PackageId, string Status, IReadOnlyList<OrderStatusLine> Lines, string? InvoiceNumber = null);

public record OrderStatusLine(string LineId, int Quantity);

/// <summary>Tüm pazaryeri adapter'larının uyması gereken ortak sözleşme.</summary>
public interface IMarketplaceClient
{
    string Channel { get; }
    Task<BatchResult> SendProductsAsync(IReadOnlyList<ProductItem> products, CancellationToken ct = default);
    Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> items, CancellationToken ct = default);
    Task<BatchStatus> GetBatchResultAsync(string batchId, CancellationToken ct = default);
    Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken ct = default);
    Task UpdateOrderStatusAsync(OrderStatusUpdate update, CancellationToken ct = default);
}

public interface IMarketplaceClientFactory
{
    IMarketplaceClient Create(MarketplaceCredentials credentials);
}

/// <summary>Kanala gidip dönen bir HTTP çağrısının kaydı.</summary>
public record ApiCallEntry(
    string Channel,
    string SellerId,
    string Method,
    string Url,
    string? RequestBody,
    int? StatusCode,
    string? ResponseBody,
    long DurationMs,
    string? Error);

public interface IApiCallLogger
{
    Task LogAsync(ApiCallEntry entry);
}

/// <summary>Kanal 2xx dışı yanıt verdiğinde fırlatılır.</summary>
public class MarketplaceApiException(string channel, int statusCode, string body)
    : Exception($"{channel} API hatası {statusCode}: {(body.Length > 500 ? body[..500] : body)}")
{
    public string Channel { get; } = channel;
    public int StatusCode { get; } = statusCode;
    public string Body { get; } = body;
}
