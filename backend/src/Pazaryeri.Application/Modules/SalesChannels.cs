namespace Pazaryeri.Application.Modules;

public static class SalesChannelIds
{
    public const string Marketplace = "marketplace";
    public const string B2B = "b2b";
    public const string B2C = "b2c";
}

/// <summary>
/// Satış kanalı: kiracının sattığı yer (pazaryerleri, B2B bayi sitesi, B2C mağaza). Her kanal bir grup modüldür;
/// kurulumda kanal seçilince bu modüller açılır, seçilmeyince (başka bir kanal da kullanmıyorsa) kapanır.
/// </summary>
/// <param name="KeyModule">Kanalın "açık" sayılmasını belirleyen ana modül.</param>
/// <param name="Modules">Kanalın ihtiyaç duyduğu modüller (ortak olanlar başka kanallarla paylaşılır).</param>
/// <param name="Available">false ise kanal tanımlıdır ama henüz seçilemez ("yakında").</param>
public record SalesChannelDef(string Id, string Name, string Description, string KeyModule, IReadOnlyList<string> Modules, bool Available);

/// <summary>Seçime göre açılacak ve kapanacak modüller (bağımlılık sırasıyla).</summary>
public record SalesChannelPlan(IReadOnlyList<string> Enable, IReadOnlyList<string> Disable);

public static class SalesChannelCatalog
{
    public static readonly IReadOnlyList<SalesChannelDef> All =
    [
        new(SalesChannelIds.Marketplace, "Pazaryerleri", "Trendyol, Hepsiburada, N11, Amazon gibi pazaryerlerinde satış; stok ve fiyat senkronu, siparişler.",
            ModuleIds.Marketplace, [ModuleIds.Marketplace, ModuleIds.StockPrice, ModuleIds.Orders], true),
        new(SalesChannelIds.B2B, "B2B (bayi sitesi)", "Bayilerin giriş yapıp özel fiyatla sipariş verdiği site; bayi yönetimi ve fiyat listeleri.",
            ModuleIds.B2BStore, [ModuleIds.Dealers, ModuleIds.PriceLists, ModuleIds.B2BStore, ModuleIds.Orders], true),
        new(SalesChannelIds.B2C, "B2C (son tüketici mağazası)", "Herkese açık mağaza: müşteriler hesapla ya da misafir olarak alışveriş yapar.",
            ModuleIds.B2CStore, [ModuleIds.B2CStore, ModuleIds.Orders], true),
    ];

    public static SalesChannelDef? Find(string id) => All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Kanalın ana modülü açıksa kanal açık sayılır.</summary>
    public static bool IsOn(SalesChannelDef channel, IReadOnlySet<string> enabledModules) => enabledModules.Contains(channel.KeyModule);

    /// <summary>Seçili kanallara göre kanal modüllerinin hedef kümesi.</summary>
    public static HashSet<string> DesiredModules(IEnumerable<string> selectedChannels) =>
        selectedChannels.Select(Find).OfType<SalesChannelDef>().SelectMany(c => c.Modules).ToHashSet();

    /// <summary>
    /// Mevcut açık modüllerden seçime geçiş planı. Yalnızca kanal modüllerine dokunulur (Çekirdek, Faturalar gibi diğerleri korunur);
    /// kapanan bir modüle bağlı açık modüller de (ör. Orders kapanınca Faturalar) birlikte kapanır. Sıralar bağımlılığa uygundur.
    /// </summary>
    public static SalesChannelPlan Plan(IEnumerable<string> selectedChannels, IReadOnlySet<string> currentlyEnabled)
    {
        var desired = DesiredModules(selectedChannels);
        var managed = All.SelectMany(c => c.Modules).ToHashSet();

        var disable = managed.Where(m => currentlyEnabled.Contains(m) && !desired.Contains(m)).ToHashSet();
        // Kapanana bağlı açık modüller de kapanır (yineleyerek, zincirleme bağımlılıklar için).
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var m in ModuleCatalog.All.Where(m => !m.Core && currentlyEnabled.Contains(m.Id) && !disable.Contains(m.Id) && m.DependsOn.Any(disable.Contains)))
                grew |= disable.Add(m.Id);
        }

        var enable = desired.Where(m => !currentlyEnabled.Contains(m)).ToHashSet();
        return new SalesChannelPlan(Order(enable, reverse: false), Order(disable, reverse: true));
    }

    private static int DepthOf(string id, int guard) =>
        guard > 10 ? 0 : 1 + (ModuleCatalog.Find(id)?.DependsOn.Select(d => DepthOf(d, guard + 1)).DefaultIfEmpty(0).Max() ?? 0);

    /// <summary>Bağımlılık derinliğine göre sıralar: açarken bağımlılıklar önce, kapatırken bağımlılar önce.</summary>
    private static List<string> Order(HashSet<string> ids, bool reverse)
    {
        var depth = new Func<string, int>(id => DepthOf(id, 0));
        var ordered = ids.OrderBy(depth).ThenBy(i => i, StringComparer.Ordinal).ToList();
        if (reverse) ordered.Reverse();
        return ordered;
    }
}

public record SalesChannelStatus(string Id, string Name, string Description, bool Available, bool Enabled, IReadOnlyList<string> Modules);

/// <param name="Completed">İlk kurulum sihirbazı tamamlandı mı.</param>
public record SetupState(bool Completed, IReadOnlyList<SalesChannelStatus> Channels);

public enum SetupResult { Applied, UnknownChannel, ChannelNotAvailable, Failed }

public record SetupOutcome(SetupResult Result, string? Detail = null);

/// <summary>Kurulum: kiracının satış kanallarını okur ve seçime göre modülleri toplu açıp kapatır.</summary>
public interface ISetupService
{
    Task<SetupState> GetAsync(CancellationToken ct = default);

    /// <summary>Kanal seçimini uygular ve kurulumu tamamlandı sayar. Seçim boş olabilir (yalnızca çekirdek).</summary>
    Task<SetupOutcome> ApplyAsync(IReadOnlyCollection<string> channels, CancellationToken ct = default);
}
