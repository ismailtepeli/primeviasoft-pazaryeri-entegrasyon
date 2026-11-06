namespace Pazaryeri.Application.Feeds;

/// <summary>Besleme alanlarının sistemdeki karşılıkları. Zorunlu üçü: barkod, ad, fiyat.</summary>
public static class FeedRoles
{
    public const string Barcode = "barcode";
    public const string Name = "name";
    public const string Price = "price";
    public const string Stock = "stock";
    public const string Brand = "brand";
    public const string Category = "category";
    public const string Images = "images";
    public const string Description = "description";
    public const string Vat = "vat";
    public const string MainId = "mainId";

    public static readonly string[] Required = [Barcode, Name, Price];
    public static readonly string[] All = [Barcode, Name, Price, Stock, Brand, Category, Images, Description, Vat, MainId];
    /// <summary>Birden çok XML yoluyla beslenebilen roller (Resim1, Resim2 gibi).</summary>
    public static readonly string[] MultiPath = [Images];

    public static string Label(string role) => role switch
    {
        Barcode => "Barkod / ürün kodu", Name => "Ürün adı", Price => "Fiyat", Stock => "Stok", Brand => "Marka", Category => "Kategori",
        Images => "Görseller", Description => "Açıklama", Vat => "KDV oranı", MainId => "Ürün ana kodu (varyant grubu)", _ => role,
    };
}

/// <param name="Samples">Alanın ilk birkaç dolu örnek değeri.</param>
/// <param name="Repeated">Ürün başına birden çok kez geliyor (görsel listesi gibi).</param>
public record FeedFieldInfo(string Path, IReadOnlyList<string> Samples, bool Repeated);

/// <param name="Count">XML'de bu öğeden kaç adet olduğu.</param>
public record FeedCandidate(string Path, int Count, int Score);

/// <summary>Eşlemeyle okunan bir ürünün normalleştirilmiş hâli; sorun varsa kayıt dışı bırakılır.</summary>
public record FeedRow(string Barcode, string Name, decimal Price, int? Stock, bool? InStock, string? Brand, string? Category, IReadOnlyList<string> Images, string? Description, int? Vat, string? MainId);

public record FeedPreviewRow(string? Barcode, string? Name, decimal? Price, int? Stock, string? Brand, string? Category, int ImageCount, IReadOnlyList<string> Problems);

/// <param name="Mapping">Rol → XML yolları.</param>
/// <param name="Missing">Eşlenemeyen zorunlu roller.</param>
public record FeedAnalysis(string Format, string ItemPath, int ItemCount, IReadOnlyList<FeedCandidate> Candidates, IReadOnlyList<FeedFieldInfo> Fields,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Mapping, IReadOnlyList<string> Missing, IReadOnlyList<FeedPreviewRow> Preview);

public record FeedAnalyzeRequest(string? Url, string? ItemPath, Dictionary<string, List<string>>? Mapping, bool PriceIncludesVat = true, int DefaultVatRate = 20, int AvailableStock = 10);

public record ProductFeedInput(string? Name, string? Url, bool Enabled, string? ItemPath, Dictionary<string, List<string>>? Mapping, decimal MarkupPercent,
    bool PriceIncludesVat, int DefaultVatRate, int AvailableStock, bool SyncPrice, bool SyncStock, bool ZeroMissing, bool PublishB2C, bool PublishB2B, int IntervalMinutes);

public record FeedRowError(int Item, string Key, string Message);

public record ProductFeedDto(Guid Id, string Name, string UrlDisplay, bool Enabled, string ItemPath, IReadOnlyDictionary<string, IReadOnlyList<string>> Mapping, string? Format,
    decimal MarkupPercent, bool PriceIncludesVat, int DefaultVatRate, int AvailableStock, bool SyncPrice, bool SyncStock, bool ZeroMissing, bool PublishB2C, bool PublishB2B,
    int IntervalMinutes, DateTime? LastRunAt, bool Running, string? LastStatus, int LastCreated, int LastUpdated, int LastSkipped, int LastFailed, string? LastError,
    IReadOnlyList<FeedRowError> LastErrors, int ProductCount);

public record FeedRunResult(string Status, int Created, int Updated, int Skipped, int Failed, int ZeroedMissing, IReadOnlyList<FeedRowError> Errors);

/// <summary>Besleme adresi okunamadığında ya da XML tanınamadığında (kullanıcıya gösterilebilir mesajla) fırlatılır.</summary>
public class FeedException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
