using Pazaryeri.Domain;

namespace Pazaryeri.Application.Catalog;

public record CatalogCategory(long Id, long? ParentId, string Name);

public record CatalogBrand(long Id, string Name);

public record CatalogValue(long Id, string Name);

/// <param name="Values">Kanalın özellik yanıtında gelen değerler; boşsa ayrıca sorgulanır.</param>
public record CatalogAttribute(long Id, string Name, bool Required, bool AllowCustom, bool Varianter, bool Slicer, IReadOnlyList<CatalogValue> Values);

/// <summary>Kanalın katalog (kategori, marka, özellik) uçlarını okuyan adapter yeteneği.</summary>
public interface IChannelCatalogClient
{
    /// <summary>Kategori ağacını düz liste olarak döner.</summary>
    Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken ct = default);

    /// <summary>Tüm markaları sayfa sayfa döner; son sayfaya kadar devam eder.</summary>
    IAsyncEnumerable<IReadOnlyList<CatalogBrand>> GetBrandPagesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CatalogAttribute>> GetCategoryAttributesAsync(long categoryId, CancellationToken ct = default);

    Task<IReadOnlyList<CatalogValue>> GetAttributeValuesAsync(long categoryId, long attributeId, CancellationToken ct = default);
}

/// <param name="Added">Yeni görülen kayıt sayısı (geri gelenler dahil).</param>
public record CatalogSyncSummary(CatalogKind Kind, int Added, int Changed, int Removed);

public interface IChannelCatalogService
{
    /// <summary>Kategori ağacını yeniler; ağaç boş dönerse mevcut veri korunur ve hata verilir.</summary>
    Task<CatalogSyncSummary> SyncCategoriesAsync(string channel, IChannelCatalogClient client, CancellationToken ct = default);

    Task<CatalogSyncSummary> SyncBrandsAsync(string channel, IChannelCatalogClient client, CancellationToken ct = default);

    /// <summary>Verilen kategorilerin özelliklerini ve değerlerini yeniler.</summary>
    Task<CatalogSyncSummary> SyncAttributesAsync(string channel, IChannelCatalogClient client, IReadOnlyList<long> categoryIds, CancellationToken ct = default);

    /// <summary>Özellikleri önbelleğe alınmış ama bir haftadan eskimiş kategoriler.</summary>
    Task<List<long>> StaleAttributeCategoriesAsync(string channel, CancellationToken ct = default);
}
