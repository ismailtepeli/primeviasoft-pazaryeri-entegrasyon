namespace Pazaryeri.Application.Products;

/// <summary>Toplu fiyat değişikliği türü: Percent (yüzde artış/azalış), Amount (tutar ekle/çıkar), Set (sabit fiyat).</summary>
public static class PriceChangeModes
{
    public const string Percent = "percent";
    public const string Amount = "amount";
    public const string Set = "set";
}

public record BulkPriceInput(List<Guid>? ProductIds, string? Mode, decimal Value);

public record PriceChangeSkipped(string Name, string Reason);

/// <param name="Unchanged">Yeni fiyatı eskisiyle aynı çıkan ürün sayısı.</param>
public record PriceChangeResult(int Updated, int Unchanged, IReadOnlyList<PriceChangeSkipped> Skipped);

/// <param name="Row">Excel'deki satır numarası (başlık 1. satır).</param>
public record PriceImportError(int Row, string Barcode, string Message);

public record PriceImportResult(int Updated, int Unchanged, IReadOnlyList<PriceImportError> Errors);

public record ProductPriceDto(Guid Id, decimal Price);

/// <summary>İstek geçersizse (boş seçim, hatalı tutar, okunamayan dosya) fırlatılır; <see cref="Status"/> HTTP durumudur.</summary>
public class ProductPriceException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Ürün listesinden fiyat güncelleme: tek ürün, seçili ürünlere toplu kural ve Excel ile.</summary>
public interface IProductPriceService
{
    public const int MaxBulk = 1000;
    public const int MaxSheetRows = 20000;

    Task<ProductPriceDto> SetPriceAsync(Guid productId, decimal price, CancellationToken ct = default);

    Task<PriceChangeResult> BulkChangeAsync(BulkPriceInput input, CancellationToken ct = default);

    /// <summary>Barkod, ürün adı ve mevcut fiyatı içeren, doldurulup geri yüklenebilir sayfa.</summary>
    Task<byte[]> BuildSheetAsync(CancellationToken ct = default);

    Task<PriceImportResult> ImportAsync(Stream xlsx, CancellationToken ct = default);
}
