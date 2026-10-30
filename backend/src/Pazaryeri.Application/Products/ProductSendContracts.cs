namespace Pazaryeri.Application.Products;

/// <summary>ProductSend işinin girdisi (SyncJob.Payload).</summary>
public record ProductSendPayload(List<Guid> ProductIds);

/// <param name="Jobs">Kuyruğa alınan iş sayısı (her iş en fazla <see cref="IProductSendService.MaxItemsPerRequest"/> ürün taşır).</param>
/// <param name="Products">Gönderime alınan ürün sayısı.</param>
/// <param name="Unknown">Bulunamadığı için atlanan ürün sayısı.</param>
/// <param name="Blocked">Eksik veri (kategori eşlemesi, zorunlu özellik…) nedeniyle kuyruğa alınmayan ürün sayısı; "Eksik" olarak işaretlenir.</param>
public record ProductSendResult(int Jobs, int Products, int Unknown, int Blocked = 0);

public interface IProductSendService
{
    /// <summary>Trendyol createProducts isteği başına en fazla bu kadar ürün alır.</summary>
    const int MaxItemsPerRequest = 1000;

    /// <summary>Kanalın toplu işlem sonucunu beklemek için tanınan süre; dolarsa ürünler hatalı sayılır.</summary>
    static readonly TimeSpan BatchTimeout = TimeSpan.FromHours(4);

    /// <summary>Seçili ürünleri kanala göndermek üzere iş(ler) olarak kuyruğa alır.</summary>
    Task<ProductSendResult> EnqueueAsync(string channel, IReadOnlyList<Guid> productIds, CancellationToken ct = default);
}
