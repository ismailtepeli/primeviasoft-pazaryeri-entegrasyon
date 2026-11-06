using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Feeds;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Quartz;

namespace Pazaryeri.Infrastructure.Feeds;

/// <summary>
/// Dropshipping XML beslemeleri: adresi çözümler, eşlemeyi saklar, ürünleri barkoda göre ekler/günceller. Yalnızca bu beslemenin eklediği ürünler
/// güncellenir; elle eklenmiş ya da başka beslemedeki aynı barkod atlanır ve raporlanır. Ad/açıklama/görsel/marka/kategori yalnızca ilk eklemede yazılır;
/// sonraki çalışmalarda stok ve fiyat (ayara göre) güncellenir, beslemeden kalkan ürünlerin stoğu sıfırlanır.
/// </summary>
public class ProductFeedService(AppDbContext db, FeedFetcher fetcher, TimeProvider time, ILogger<ProductFeedService> logger)
{
    public const int MaxFeeds = 20;
    public const int Batch = 500;
    public const int StoredErrors = 50;
    public static readonly TimeSpan StaleRun = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    // ---------- çözümleme ----------

    public async Task<FeedAnalysis> AnalyzeAsync(FeedAnalyzeRequest request, CancellationToken ct)
    {
        await using var file = await fetcher.DownloadAsync(request.Url ?? "", ct);
        var options = new FeedOptions(request.PriceIncludesVat, request.DefaultVatRate, request.AvailableStock);
        return await Task.Run(() => XmlFeed.Analyze(file, request.ItemPath, request.Mapping, options), ct);
    }

    /// <summary>Kayıtlı beslemenin adresini (anahtarı yönetim ekranına göstermeden) yeniden çözümler; öğe yolu ve eşleme istekten gelen değerlerle denenir.</summary>
    public async Task<FeedAnalysis> AnalyzeStoredAsync(Guid id, FeedAnalyzeRequest request, CancellationToken ct)
    {
        var feed = await db.ProductFeeds.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new FeedException(404, "Besleme bulunamadı.");
        return await AnalyzeAsync(request with { Url = feed.Url }, ct);
    }

    // ---------- kayıt ----------

    private static Dictionary<string, List<string>> ParseMapping(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string DisplayUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Path) + (string.IsNullOrEmpty(u.Query) ? "" : "?…") : url;

    private ProductFeedDto ToDto(ProductFeed f, int productCount)
    {
        var errors = string.IsNullOrEmpty(f.LastErrorsJson) ? [] : JsonSerializer.Deserialize<List<FeedRowError>>(f.LastErrorsJson, Json) ?? [];
        return new ProductFeedDto(f.Id, f.Name, DisplayUrl(f.Url), f.Enabled, f.ItemPath,
            ParseMapping(f.MappingJson).ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value), f.Format, f.MarkupPercent, f.PriceIncludesVat, f.DefaultVatRate,
            f.AvailableStock, f.SyncPrice, f.SyncStock, f.ZeroMissing, f.PublishB2C, f.PublishB2B, f.IntervalMinutes, f.LastRunAt,
            f.RunningSince is { } r && Now() - r < StaleRun, f.LastStatus, f.LastCreated, f.LastUpdated, f.LastSkipped, f.LastFailed, f.LastError, errors, productCount);
    }

    public async Task<IReadOnlyList<ProductFeedDto>> ListAsync(CancellationToken ct)
    {
        var feeds = await db.ProductFeeds.AsNoTracking().OrderBy(f => f.Name).ToListAsync(ct);
        var counts = await db.Products.AsNoTracking().Where(p => p.FeedId != null).GroupBy(p => p.FeedId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        return feeds.Select(f => ToDto(f, counts.GetValueOrDefault(f.Id))).ToList();
    }

    public async Task<ProductFeedDto> SaveAsync(Guid? id, ProductFeedInput input, CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > ProductFeed.MaxNameLength) throw new FeedException(400, $"Besleme adı 1 ile {ProductFeed.MaxNameLength} karakter arasında olmalı.");
        if (input.MarkupPercent is < -90 or > 1000) throw new FeedException(400, "Kâr yüzdesi -90 ile 1000 arasında olmalı.");
        if (input.DefaultVatRate is < 0 or > 100) throw new FeedException(400, "KDV oranı 0 ile 100 arasında olmalı.");
        if (input.AvailableStock is < 0 or > 100_000) throw new FeedException(400, "Stokta var adedi 0 ile 100.000 arasında olmalı.");
        if (input.IntervalMinutes < ProductFeed.MinIntervalMinutes || input.IntervalMinutes > ProductFeed.MaxIntervalMinutes)
            throw new FeedException(400, $"Güncelleme aralığı {ProductFeed.MinIntervalMinutes} dakika ile 24 saat arasında olmalı.");

        ProductFeed feed;
        if (id is null)
        {
            if (await db.ProductFeeds.CountAsync(ct) >= MaxFeeds) throw new FeedException(409, $"En fazla {MaxFeeds} besleme tanımlanabilir.");
            FeedFetcher.ValidateUrl(input.Url);
            feed = new ProductFeed { Url = input.Url!.Trim() };
            db.ProductFeeds.Add(feed);
        }
        else
        {
            feed = await db.ProductFeeds.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new FeedException(404, "Besleme bulunamadı.");
            if (!string.IsNullOrWhiteSpace(input.Url) && input.Url.Trim() != feed.Url) { FeedFetcher.ValidateUrl(input.Url); feed.Url = input.Url.Trim(); }
        }

        // Öğe yolu ya da eşleme verilmediyse adresten otomatik tanınır.
        var itemPath = input.ItemPath?.Trim() ?? "";
        var mapping = input.Mapping;
        if (itemPath.Length == 0 || mapping is not { Count: > 0 })
        {
            if (id is not null && feed.ItemPath.Length > 0 && itemPath.Length == 0 && mapping is null) { itemPath = feed.ItemPath; mapping = ParseMapping(feed.MappingJson); }
            else
            {
                var analysis = await AnalyzeAsync(new FeedAnalyzeRequest(feed.Url, string.IsNullOrEmpty(itemPath) ? null : itemPath, null, input.PriceIncludesVat, input.DefaultVatRate, input.AvailableStock), ct);
                itemPath = analysis.ItemPath;
                mapping = analysis.Mapping.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
                feed.Format = analysis.Format;
            }
        }
        var clean = new Dictionary<string, List<string>>();
        foreach (var (role, paths) in mapping!)
        {
            if (!FeedRoles.All.Contains(role)) continue;
            var valid = paths.Where(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 300).Select(p => p.Trim()).Distinct().Take(FeedRoles.MultiPath.Contains(role) ? 12 : 1).ToList();
            if (valid.Count > 0) clean[role] = valid;
        }
        var missing = FeedRoles.Required.Where(r => !clean.ContainsKey(r)).Select(FeedRoles.Label).ToList();
        if (missing.Count > 0) throw new FeedException(422, $"Zorunlu alan eşlenmedi: {string.Join(", ", missing)}.");

        feed.Name = name; feed.Enabled = input.Enabled; feed.ItemPath = itemPath; feed.MappingJson = JsonSerializer.Serialize(clean, Json);
        feed.MarkupPercent = decimal.Round(input.MarkupPercent, 2); feed.PriceIncludesVat = input.PriceIncludesVat; feed.DefaultVatRate = input.DefaultVatRate;
        feed.AvailableStock = input.AvailableStock; feed.SyncPrice = input.SyncPrice; feed.SyncStock = input.SyncStock; feed.ZeroMissing = input.ZeroMissing;
        feed.PublishB2C = input.PublishB2C; feed.PublishB2B = input.PublishB2B; feed.IntervalMinutes = input.IntervalMinutes;
        await db.SaveChangesAsync(ct);
        return ToDto(feed, await db.Products.CountAsync(p => p.FeedId == feed.Id, ct));
    }

    /// <summary>Beslemeyi siler; eklediği ürünler katalogda kalır (elle yönetilen ürüne dönüşür).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var feed = await db.ProductFeeds.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new FeedException(404, "Besleme bulunamadı.");
        await db.Products.Where(p => p.FeedId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.FeedId, (Guid?)null), ct);
        db.ProductFeeds.Remove(feed);
        await db.SaveChangesAsync(ct);
    }

    // ---------- çalıştırma ----------

    /// <summary>Zamanı gelen etkin beslemeleri çalıştırır (kiracı bağlamı kurulmuş olmalı); çalıştırılan sayıyı döner.</summary>
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = Now();
        var feeds = await db.ProductFeeds.AsNoTracking().Where(f => f.Enabled).ToListAsync(ct);
        var due = feeds.Where(f => (f.LastRunAt is null || f.LastRunAt.Value.AddMinutes(f.IntervalMinutes) <= now) && (f.RunningSince is null || now - f.RunningSince.Value >= StaleRun)).ToList();
        foreach (var f in due)
        {
            try { await RunAsync(f.Id, ct); }
            catch (FeedException e) { logger.LogInformation("Besleme {Feed} çalıştırılamadı: {Message}", f.Name, e.Message); }
        }
        return due.Count;
    }

    private sealed record Pending(int Index, FeedRow Row);

    public async Task<FeedRunResult> RunAsync(Guid id, CancellationToken ct)
    {
        var feed = await db.ProductFeeds.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new FeedException(404, "Besleme bulunamadı.");
        var started = Now();
        if (feed.RunningSince is { } since && started - since < StaleRun) throw new FeedException(409, "Bu besleme şu anda çalışıyor.");
        feed.RunningSince = started;
        await db.SaveChangesAsync(ct);

        int created = 0, updated = 0, skipped = 0, failed = 0, zeroed = 0;
        var errors = new List<FeedRowError>();
        string status;
        string? fatal = null;
        try
        {
            var mapping = ParseMapping(feed.MappingJson);
            var options = new FeedOptions(feed.PriceIncludesVat, feed.DefaultVatRate, feed.AvailableStock);
            await using var file = await fetcher.DownloadAsync(feed.Url, ct);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var categories = await db.Categories.ToListAsync(ct);
            var pending = new List<Pending>();
            var index = 0;
            var total = 0;

            async Task FlushAsync()
            {
                if (pending.Count == 0) return;
                var (c, u, s) = await SaveBatchAsync(feed, pending, categories, errors, ct);
                created += c; updated += u; skipped += s;
                pending.Clear();
            }

            foreach (var item in XmlFeed.ReadItems(file, feed.ItemPath))
            {
                index++; total++;
                var (row, problems) = XmlFeed.Map(item, mapping, options);
                if (row is null) { failed++; AddError(errors, index, item.Values.Values.FirstOrDefault()?.FirstOrDefault() ?? "", string.Join(" ", problems)); continue; }
                if (!seen.Add(row.Barcode)) { skipped++; AddError(errors, index, row.Barcode, "Besleme içinde yinelenen barkod; ilk kayıt kullanıldı."); continue; }
                pending.Add(new Pending(index, row));
                if (pending.Count >= Batch) await FlushAsync();
            }
            await FlushAsync();

            if (total == 0) throw new FeedException(422, "Beslemede ürün öğesi bulunamadı; eşleme ya da öğe yolu artık geçerli değil. Beslemeyi yeniden çözümleyin.");
            if (seen.Count == 0) throw new FeedException(422, "Beslemedeki hiçbir ürün okunamadı; eşlemeyi kontrol edin.");
            if (feed.ZeroMissing) zeroed = await ZeroMissingAsync(feed.Id, seen, ct);
            status = failed > 0 || skipped > 0 ? "Partial" : "Success";
        }
        catch (FeedException e) { status = "Failed"; fatal = e.Message; }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Besleme {Feed} çalıştırılırken beklenmeyen hata.", feed.Name);
            status = "Failed"; fatal = "Beklenmeyen bir hata oluştu; ayrıntı sunucu günlüğünde.";
        }

        db.ChangeTracker.Clear();
        var tracked = await db.ProductFeeds.FirstAsync(f => f.Id == id, CancellationToken.None);
        tracked.RunningSince = null; tracked.LastRunAt = Now(); tracked.LastStatus = status; tracked.LastError = fatal;
        tracked.LastCreated = created; tracked.LastUpdated = updated; tracked.LastSkipped = skipped; tracked.LastFailed = failed;
        tracked.LastErrorsJson = errors.Count == 0 ? null : JsonSerializer.Serialize(errors.Take(StoredErrors), Json);
        await db.SaveChangesAsync(CancellationToken.None);
        if (fatal is not null && status == "Failed") throw new FeedException(422, fatal);
        return new FeedRunResult(status, created, updated, skipped, failed, zeroed, errors.Take(StoredErrors).ToList());
    }

    private static void AddError(List<FeedRowError> errors, int item, string key, string message)
    {
        if (errors.Count < 500) errors.Add(new FeedRowError(item, key.Length > 100 ? key[..100] : key, message));
    }

    private static decimal SalePrice(FeedRow row, ProductFeed feed)
    {
        var gross = feed.PriceIncludesVat ? row.Price : row.Price * (1 + (row.Vat ?? feed.DefaultVatRate) / 100m);
        return decimal.Round(gross * (1 + feed.MarkupPercent / 100m), 2, MidpointRounding.AwayFromZero);
    }

    private static int? StockOf(FeedRow row, ProductFeed feed) => row.Stock ?? row.InStock switch { true => feed.AvailableStock, false => 0, _ => null };

    private async Task<(int Created, int Updated, int Skipped)> SaveBatchAsync(ProductFeed feed, List<Pending> pending, List<Category> categories, List<FeedRowError> errors, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var barcodes = pending.Select(p => p.Row.Barcode).ToList();
                var existing = (await db.Products.Where(p => barcodes.Contains(p.Barcode)).ToListAsync(ct)).ToDictionary(p => p.Barcode, StringComparer.OrdinalIgnoreCase);
                int created = 0, updated = 0, skipped = 0;
                foreach (var (index, row) in pending)
                {
                    if (existing.TryGetValue(row.Barcode, out var product))
                    {
                        if (product.FeedId != feed.Id)
                        {
                            skipped++;
                            AddError(errors, index, row.Barcode, product.FeedId is null ? "Bu barkod elle eklenmiş bir üründe kayıtlı; dokunulmadı." : "Bu barkod başka bir beslemenin ürününde kayıtlı; dokunulmadı.");
                            continue;
                        }
                        if (feed.SyncStock && StockOf(row, feed) is { } stock) product.Stock = stock;
                        if (feed.SyncPrice) { product.Cost = row.Price; product.Price = SalePrice(row, feed); }
                        updated++;
                        continue;
                    }
                    product = new Product
                    {
                        FeedId = feed.Id, Barcode = row.Barcode, Name = row.Name, Brand = row.Brand, Description = row.Description, MainId = row.MainId,
                        VatRate = row.Vat ?? feed.DefaultVatRate, Stock = StockOf(row, feed) ?? 0, Cost = row.Price, Price = SalePrice(row, feed),
                        B2BVisible = feed.PublishB2B, B2CVisible = feed.PublishB2C,
                    };
                    if (row.Category is not null) product.CategoryId = EnsureCategory(row.Category, categories).Id;
                    db.Products.Add(product);
                    for (var i = 0; i < row.Images.Count; i++) db.ProductImages.Add(new ProductImage { ProductId = product.Id, Kind = ImageKind.Url, ExternalUrl = row.Images[i], Position = i });
                    created++;
                }
                await db.SaveChangesAsync(ct);
                return (created, updated, skipped);
            }
            catch (DbUpdateConcurrencyException) when (attempt == 0)
            {
                // Aynı anda gelen bir sipariş stoğu değiştirmiş olabilir; kayıtları yeniden okuyup bir kez daha denenir.
                db.ChangeTracker.Clear();
                var fresh = await db.Categories.ToListAsync(ct);
                categories.Clear(); categories.AddRange(fresh);
            }
        }
    }

    /// <summary>"Giyim > Tişört" yolunu kategori kaydına çevirir; olmayan basamakları oluşturur.</summary>
    private Category EnsureCategory(string path, List<Category> categories)
    {
        Category? current = null;
        foreach (var part in path.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parent = current?.Id;
            var next = categories.FirstOrDefault(c => c.ParentId == parent && string.Equals(c.Name, part, StringComparison.CurrentCultureIgnoreCase));
            if (next is null)
            {
                next = new Category { Name = part, ParentId = parent };
                db.Categories.Add(next);
                categories.Add(next);
            }
            current = next;
        }
        return current!;
    }

    private async Task<int> ZeroMissingAsync(Guid feedId, HashSet<string> seen, CancellationToken ct)
    {
        var ours = await db.Products.AsNoTracking().Where(p => p.FeedId == feedId && p.Stock > 0).Select(p => new { p.Id, p.Barcode }).ToListAsync(ct);
        var ids = ours.Where(p => !seen.Contains(p.Barcode)).Select(p => p.Id).ToList();
        foreach (var chunk in ids.Chunk(1000))
            await db.Products.Where(p => chunk.Contains(p.Id)).ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, 0), ct);
        return ids.Count;
    }
}

/// <summary>Zamanı gelen XML beslemelerini periyodik çalıştırır (10 dk); her besleme kendi aralığına göre çalışır.</summary>
[DisallowConcurrentExecution]
public class ProductFeedSyncJob(IServiceScopeFactory scopes, ILogger<ProductFeedSyncJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct)
    {
        List<Guid> tenants;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenants = await db.ProductFeeds.IgnoreQueryFilters().AsNoTracking().Where(f => f.Enabled).Select(f => f.TenantId).Distinct().ToListAsync(ct);
        }
        foreach (var tenantId in tenants)
        {
            try
            {
                using var _ = AmbientTenant.Use(tenantId);
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ProductFeedService>().RunDueAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "XML besleme senkronu çalıştırılamadı (kiracı {Tenant}).", tenantId);
            }
        }
    }
}
