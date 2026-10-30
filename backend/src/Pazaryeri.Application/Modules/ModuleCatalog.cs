using Pazaryeri.Domain;

namespace Pazaryeri.Application.Modules;

public static class ModuleIds
{
    public const string Core = "core";
    public const string Marketplace = "marketplace";
    public const string StockPrice = "stock-price";
    public const string Orders = "orders";
    public const string Invoices = "invoices";
    public const string Dealers = "dealers";
    public const string PriceLists = "price-lists";
    public const string B2BStore = "b2b-store";
}

/// <param name="Core">Kapatılamaz.</param>
/// <param name="DefaultEnabled">Kiracı için kayıt yoksa durum.</param>
/// <param name="DependsOn">Açık olması gereken modüller.</param>
public record ModuleDef(string Id, string Name, string Description, bool Core, bool DefaultEnabled, IReadOnlyList<string> DependsOn);

/// <summary>Kod içindeki modül tanımları (ADR 0001). Kimlikler kararlıdır, yeniden adlandırılmaz.</summary>
public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDef> All =
    [
        new(ModuleIds.Core, "Çekirdek", "Ürünler, kategoriler, görseller, stok havuzu ve genel bakış.", true, true, []),
        new(ModuleIds.Marketplace, "Pazaryeri entegrasyonu", "Mağazalar, kategori/özellik eşleme, katalog ve ürün gönderimi.", false, true, [ModuleIds.Core]),
        new(ModuleIds.StockPrice, "Stok ve fiyat senkronu", "Kanal stok kuralları, fiyat kuralları ve stok-fiyat senkronu.", false, true, [ModuleIds.Marketplace]),
        new(ModuleIds.Orders, "Siparişler", "Sipariş listesi, sipariş senkronu ve iadeler.", false, true, [ModuleIds.Core]),
        new(ModuleIds.Invoices, "Faturalar", "e-Fatura ve fatura takibi.", false, true, [ModuleIds.Orders]),
        new(ModuleIds.Dealers, "Bayi yönetimi", "Bayi firmalar, bayi kullanıcıları ve bayi grupları.", false, false, [ModuleIds.Core]),
        new(ModuleIds.PriceLists, "Fiyat listeleri", "Bayi gruplarına özel fiyat, iskonto, minimum ve koli adedi.", false, false, [ModuleIds.Dealers]),
        new(ModuleIds.B2BStore, "B2B bayi sitesi", "Bayilerin ürünleri gördüğü ve sipariş verdiği site.", false, false, [ModuleIds.Dealers, ModuleIds.Orders]),
    ];

    public static ModuleDef? Find(string id) => All.FirstOrDefault(m => m.Id == id);

    /// <summary>Senkron iş türünün ait olduğu modül; tanımsızsa çekirdek.</summary>
    public static string ModuleFor(SyncJobType type) => type switch
    {
        SyncJobType.CatalogSync or SyncJobType.ProductSend => ModuleIds.Marketplace,
        SyncJobType.StockPriceSync => ModuleIds.StockPrice,
        SyncJobType.OrderSync => ModuleIds.Orders,
        SyncJobType.InvoiceSend => ModuleIds.Invoices,
        _ => ModuleIds.Core,
    };

    /// <summary>Kayıtlı durumlara (modül kimliği → açık mı) varsayılanları uygulayıp açık modüllerin kümesini verir.</summary>
    public static HashSet<string> Resolve(IReadOnlyDictionary<string, bool> saved) =>
        All.Where(m => m.Core || (saved.TryGetValue(m.Id, out var on) ? on : m.DefaultEnabled)).Select(m => m.Id).ToHashSet();

    /// <summary>Açılamama nedenleri: eksik bağımlılıklar. Boşsa açılabilir.</summary>
    public static IReadOnlyList<string> MissingDependencies(string id, IReadOnlySet<string> enabled) =>
        Find(id)?.DependsOn.Where(d => !enabled.Contains(d)).ToList() ?? [];

    /// <summary>Kapatılamama nedenleri: bu modüle bağlı açık modüller. Boşsa kapatılabilir.</summary>
    public static IReadOnlyList<string> EnabledDependents(string id, IReadOnlySet<string> enabled) =>
        All.Where(m => enabled.Contains(m.Id) && m.DependsOn.Contains(id)).Select(m => m.Id).ToList();
}

public enum ModuleChangeResult { Changed, Unchanged, Unknown, CoreCannotDisable, MissingDependencies, HasDependents }

public record ModuleChange(ModuleChangeResult Result, IReadOnlyList<string> Related);

public record ModuleStatus(string Id, string Name, string Description, bool Core, bool Enabled, IReadOnlyList<string> DependsOn);

public interface IModuleService
{
    /// <summary>Kiracının (verilmezse oturumdaki) açık modülleri.</summary>
    Task<IReadOnlySet<string>> EnabledAsync(Guid? tenantId = null, CancellationToken ct = default);
    Task<bool> IsEnabledAsync(string moduleId, Guid? tenantId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ModuleStatus>> ListAsync(CancellationToken ct = default);
    /// <summary>Oturumdaki kiracı için modülü açar/kapatır; kurallara uymuyorsa değiştirmez ve nedeni döner.</summary>
    Task<ModuleChange> SetAsync(string moduleId, bool enabled, CancellationToken ct = default);
}
