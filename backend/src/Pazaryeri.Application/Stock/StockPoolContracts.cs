namespace Pazaryeri.Application.Stock;

public record StockLine(string Barcode, int Quantity);

public enum StockOutcome
{
    /// <summary>Stok değişti.</summary>
    Applied,
    /// <summary>Bu olay daha önce işlenmişti; stok değişmedi.</summary>
    Duplicate,
    /// <summary>Barkoda ait ürün katalogda yok.</summary>
    UnknownBarcode,
    /// <summary>Geri eklenecek bir satış bulunamadı ya da tamamı zaten geri eklenmişti.</summary>
    NothingToRestore,
    /// <summary>Adet geçersiz (sıfır ya da negatif).</summary>
    InvalidQuantity,
    /// <summary>Ortak stok havuzu ayarı kapalı: satış stoğu etkilemez.</summary>
    PoolDisabled,
}

/// <param name="Applied">Stoğa yansıyan adet (pozitif); fazla satışta istenenden küçük olabilir.</param>
/// <param name="Oversold">Stok yetmediği için düşülemeyen adet.</param>
public record StockLineResult(string Barcode, StockOutcome Outcome, int Applied = 0, int StockAfter = 0, int Oversold = 0);

/// <param name="Reference">Satış referansı; iptalde aynı referansla geri eklenir.</param>
/// <param name="Keep">Satılamaz, stokta bırakılacak adet (güvenlik stoğu).</param>
public record StockReserveLine(string Barcode, int Quantity, string Reference, int Keep = 0);

/// <param name="Available">Satılabilir adet (stok - güvenlik stoğu, en az 0).</param>
public record StockShortage(string Barcode, int Requested, int Available);

/// <param name="Applied">Stok gerçekten düşüldü mü (havuz kapalıysa ya da olay tekrarsa false).</param>
public record StockReserveResult(bool Success, bool Applied, IReadOnlyList<StockShortage> Shortages);

/// <summary>Siparişlerin stok havuzuna etkisi: satışta düş, iptal/iadede geri ekle, ve diğer kanalların stokunu güncelletir.</summary>
public interface IStockPoolService
{
    /// <summary>
    /// <paramref name="reference"/> sipariş/paket kimliğidir; aynı (kanal, referans, barkod) tekrar gelirse stok bir kez düşer.
    /// Ortak havuz ayarı açıksa, değişiklik olduysa diğer bağlı kanallar için stok-fiyat senkronu kuyruğa alınır.
    /// </summary>
    Task<IReadOnlyList<StockLineResult>> DeductForSaleAsync(string channel, string reference, IReadOnlyList<StockLine> lines, CancellationToken ct = default);

    /// <summary>
    /// Fazla satışa izin vermeyen (B2B) düşüm: tüm satırlar tek işlemde, ya hep ya hiç düşülür. Her satır veritabanında koşullu
    /// tek güncellemeyle (stok - güvenlik stoğu >= adet) alındığından eşzamanlı siparişlerde stok eksiye düşmez ve yetersiz kalan
    /// sipariş stoğu, başkasının siparişi için bile olsa, geçici olarak tutmaz. Yetmeyen satırlar kalan adetle birlikte döner.
    /// </summary>
    Task<StockReserveResult> DeductAllOrNothingAsync(string channel, IReadOnlyList<StockReserveLine> lines, CancellationToken ct = default);

    /// <summary>
    /// İptal/iadede, <paramref name="saleReference"/> ile düşülmüş stoğu geri ekler. <paramref name="eventReference"/> iptal/iade
    /// olayının kimliğidir (kısmi iadeler için ayrı); aynı olay tekrar gelirse etkisizdir. Satılandan fazlası geri eklenmez.
    /// </summary>
    Task<IReadOnlyList<StockLineResult>> RestoreAsync(string channel, string saleReference, string eventReference, IReadOnlyList<StockLine> lines, CancellationToken ct = default);
}
