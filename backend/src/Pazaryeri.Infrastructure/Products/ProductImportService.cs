using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Products;

/// <summary>
/// Excel'den toplu ürün yükleme. Geçerli satırlar kaydedilir (barkod varsa güncellenir), geçersiz satırlar
/// numarasıyla raporlanır. Aynı ürün ana kodunu taşıyan satırlar tek ürünün varyantı sayılır.
/// </summary>
public class ProductImportService(AppDbContext db) : IProductImportService
{
    public const int MaxRows = 5000;
    private const string MainIdHeader = "Ürün Ana Kodu";
    private const string NameHeader = "Ürün Adı";
    private const string BarcodeHeader = "Barkod";
    private const string VariantHeader = "Varyant";
    private const string StockHeader = "Stok";
    private const string PriceHeader = "Fiyat";
    private const string BrandHeader = "Marka";
    private const string CategoryHeader = "Kategori";
    private const string ImagesHeader = "Görseller";
    private static readonly string[] Headers = [MainIdHeader, NameHeader, BarcodeHeader, VariantHeader, StockHeader, PriceHeader, BrandHeader, CategoryHeader, ImagesHeader];
    private static readonly string[] Optional = [MainIdHeader, VariantHeader, BrandHeader, CategoryHeader, ImagesHeader];

    public byte[] BuildTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Ürünler");
        for (var i = 0; i < Headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = Headers[i];
            cell.Style.Font.Bold = true;
        }
        ws.Column(3).Style.NumberFormat.Format = "@"; // barkod metin kalsın, başındaki sıfırlar silinmesin

        // Örnek satırlar: aynı ana kod iki varyantı gruplar.
        string[][] sample =
        [
            ["TSH-001", "Basic Tişört", "8690000000011", "Beden: M, Renk: Mavi"],
            ["TSH-001", "Basic Tişört", "8690000000028", "Beden: L, Renk: Mavi"],
        ];
        string[][] extra =
        [
            ["Örnek Marka", "Giyim > Tişört", "https://cdn.example.com/tisort-1.jpg; https://cdn.example.com/tisort-2.jpg; https://cdn.example.com/tisort-3.jpg"],
            ["Örnek Marka", "Giyim > Tişört", ""],
        ];
        decimal[][] numbers = [[25, 199.90m], [18, 199.90m]];
        for (var r = 0; r < sample.Length; r++)
        {
            for (var c = 0; c < sample[r].Length; c++) ws.Cell(r + 2, c + 1).SetValue(sample[r][c]);
            ws.Cell(r + 2, 5).Value = numbers[r][0];
            ws.Cell(r + 2, 6).Value = numbers[r][1];
            for (var c = 0; c < extra[r].Length; c++) ws.Cell(r + 2, 7 + c).SetValue(extra[r][c]);
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private sealed record Row(int Number, string MainId, string Name, string Barcode, string? Variant, int Stock, decimal Price, string? Brand, string? Category, List<string> Images);

    public async Task<ProductImportResult> ImportAsync(Stream xlsx, CancellationToken ct = default)
    {
        XLWorkbook wb;
        try { wb = new XLWorkbook(xlsx); }
        catch (Exception) { throw new ProductImportException("Dosya okunamadı. Lütfen şablondaki .xlsx biçimini kullanın."); }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault() ?? throw new ProductImportException("Dosyada sayfa bulunamadı.");
            var used = ws.RangeUsed() ?? throw new ProductImportException("Dosya boş.");

            var columns = MapColumns(ws, used.LastColumn().ColumnNumber());
            var last = used.LastRow().RowNumber();
            if (last - 1 > MaxRows) throw new ProductImportException($"Tek seferde en fazla {MaxRows} satır yüklenebilir.");

            var errors = new List<ProductImportError>();
            var rows = new List<Row>();
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var r = 2; r <= last; r++)
            {
                if (columns.Values.All(c => ws.Cell(r, c).IsEmpty())) continue; // tamamen boş satır

                var rowNo = r;
                string Text(string header) => ws.Cell(rowNo, columns[header]).GetString().Trim();
                var barcode = Text(BarcodeHeader);
                var name = Text(NameHeader);
                var mainId = Text(MainIdHeader);
                var problems = new List<string>();

                if (name.Length == 0) problems.Add("Ürün adı boş.");
                else if (name.Length > 500) problems.Add("Ürün adı 500 karakteri aşıyor.");
                if (barcode.Length == 0) problems.Add("Barkod boş.");
                else if (barcode.Length > 100) problems.Add("Barkod 100 karakteri aşıyor.");
                else if (seen.TryGetValue(barcode, out var first)) problems.Add($"Barkod dosyada {first}. satırda da var.");
                if (mainId.Length > 100) problems.Add("Ürün ana kodu 100 karakteri aşıyor.");
                var brand = Text(BrandHeader);
                if (brand.Length > 200) problems.Add("Marka 200 karakteri aşıyor.");
                var category = Text(CategoryHeader);
                if (category.Length > 0 && !TryParseCategory(category, out _, out var categoryError)) problems.Add(categoryError!);
                var images = ParseImages(Text(ImagesHeader), problems);

                var stock = ParseStock(ws.Cell(r, columns[StockHeader]), out var stockError);
                if (stockError is not null) problems.Add(stockError);
                var price = ParsePrice(ws.Cell(r, columns[PriceHeader]), out var priceError);
                if (priceError is not null) problems.Add(priceError);

                if (problems.Count > 0) { errors.Add(new ProductImportError(r, barcode, string.Join(" ", problems))); continue; }

                seen[barcode] = r;
                var variant = Text(VariantHeader);
                rows.Add(new Row(r, mainId, name, barcode, variant.Length > 0 ? variant : null, stock, price,
                    brand.Length > 0 ? brand : null, category.Length > 0 ? category : null, images));
            }

            // Aynı ana kodlu varyantlar aynı ürün adını taşımalı; ilk satırın adı geçerli sayılır.
            var heads = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            var accepted = new List<Row>();
            foreach (var row in rows)
            {
                if (row.MainId.Length > 0)
                {
                    if (!heads.TryGetValue(row.MainId, out var head)) heads[row.MainId] = row;
                    else if (!string.Equals(head.Name, row.Name, StringComparison.Ordinal))
                    {
                        errors.Add(new ProductImportError(row.Number, row.Barcode,
                            $"Ana kod '{row.MainId}' için ürün adı {head.Number}. satırdaki '{head.Name}' ile aynı olmalı."));
                        continue;
                    }
                }
                accepted.Add(row);
            }

            var (created, updated) = await SaveAsync(accepted, ct);
            var groups = accepted.Select(a => a.MainId.Length > 0 ? "m:" + a.MainId.ToLowerInvariant() : "b:" + a.Barcode).Distinct().Count();
            return new ProductImportResult(created, updated, groups, errors.OrderBy(e => e.Row).ToList());
        }
    }

    private async Task<(int Created, int Updated)> SaveAsync(List<Row> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return (0, 0);
        var barcodes = rows.Select(r => r.Barcode).ToList();
        var existing = (await db.Products.Where(p => barcodes.Contains(p.Barcode)).ToListAsync(ct))
            .ToDictionary(p => p.Barcode, StringComparer.OrdinalIgnoreCase);

        var existingImages = (await db.ProductImages.Where(i => i.Kind == ImageKind.Url && existing.Values.Select(p => p.Id).Contains(i.ProductId)).ToListAsync(ct))
            .ToLookup(i => i.ProductId);
        var allCounts = await db.ProductImages.Where(i => existing.Values.Select(p => p.Id).Contains(i.ProductId))
            .GroupBy(i => i.ProductId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var categories = await db.Categories.ToListAsync(ct);

        int created = 0, updated = 0;
        foreach (var row in rows)
        {
            if (existing.TryGetValue(row.Barcode, out var product)) updated++;
            else { product = new Product { Barcode = row.Barcode }; db.Products.Add(product); created++; }
            if (row.Brand is not null) product.Brand = row.Brand;
            if (row.Category is not null) product.CategoryId = EnsureCategory(row.Category, categories).Id;
            AddImages(product, row.Images, existingImages[product.Id].Select(i => i.ExternalUrl!).ToHashSet(StringComparer.OrdinalIgnoreCase),
                allCounts.GetValueOrDefault(product.Id));
            product.MainId = row.MainId.Length > 0 ? row.MainId : null;
            product.Name = row.Name;
            product.Variant = row.Variant;
            product.Stock = row.Stock;
            product.Price = row.Price;
        }
        await db.SaveChangesAsync(ct);
        return (created, updated);
    }

    /// <summary>"Giyim > Tişört" yolunu kategori kaydına çevirir; olmayan basamakları oluşturur.</summary>
    private Category EnsureCategory(string path, List<Category> categories)
    {
        TryParseCategory(path, out var parts, out _);
        Category? current = null;
        foreach (var part in parts!)
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

    private static bool TryParseCategory(string path, out string[]? parts, out string? error)
    {
        parts = path.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        error = null;
        if (parts.Length == 0) { error = "Kategori yolu geçersiz."; return false; }
        if (parts.Length > 5) { error = "Kategori en fazla 5 seviye olabilir."; return false; }
        if (parts.Any(p => p.Length > 200)) { error = "Kategori adı 200 karakteri aşıyor."; return false; }
        return true;
    }

    /// <summary>Hücredeki adresleri (; ya da satır sonu ile ayrılmış) doğrular.</summary>
    private static List<string> ParseImages(string cell, List<string> problems)
    {
        var urls = cell.Split([';', (char)10, (char)13], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (urls.Count > ProductImage.MaxPerProduct) problems.Add($"En fazla {ProductImage.MaxPerProduct} görsel adresi girilebilir.");
        foreach (var u in urls.Where(u => u.Length > 2000 || !Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)))
            problems.Add($"Geçersiz görsel adresi: {(u.Length > 60 ? u[..60] + "…" : u)}");
        return urls;
    }

    /// <summary>Var olan görselleri bozmadan yeni adresleri sona ekler (üst sınır aşılırsa fazlası atlanır).</summary>
    private void AddImages(Product product, List<string> urls, HashSet<string> known, int currentCount)
    {
        foreach (var url in urls)
        {
            if (currentCount >= ProductImage.MaxPerProduct) break;
            var normalized = new Uri(url).AbsoluteUri;
            if (!known.Add(normalized)) continue;
            db.ProductImages.Add(new ProductImage { ProductId = product.Id, Kind = ImageKind.Url, ExternalUrl = normalized, Position = currentCount++ });
        }
    }

    private static Dictionary<string, int> MapColumns(IXLWorksheet ws, int lastColumn)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var c = 1; c <= lastColumn; c++)
        {
            var text = ws.Cell(1, c).GetString().Trim();
            var header = Headers.FirstOrDefault(h => string.Equals(h, text, StringComparison.OrdinalIgnoreCase));
            if (header is not null) found.TryAdd(header, c);
        }
        var missing = Headers.Where(h => !Optional.Contains(h) && !found.ContainsKey(h)).ToList();
        if (missing.Count > 0)
            throw new ProductImportException($"Başlık satırında eksik sütun: {string.Join(", ", missing)}. Şablonu indirip kullanın.");
        // Opsiyonel sütun yoksa hep boş okunan bir sütuna işaret eder.
        foreach (var optional in Optional) found.TryAdd(optional, lastColumn + 1);
        return found;
    }

    private static int ParseStock(IXLCell cell, out string? error)
    {
        error = null;
        if (!TryDecimal(cell, out var value)) { error = "Stok sayı olmalı."; return 0; }
        if (value < 0 || value != decimal.Truncate(value)) { error = "Stok sıfır ya da pozitif tam sayı olmalı."; return 0; }
        if (value > int.MaxValue) { error = "Stok çok büyük."; return 0; }
        return (int)value;
    }

    private static decimal ParsePrice(IXLCell cell, out string? error)
    {
        error = null;
        if (!TryDecimal(cell, out var value)) { error = "Fiyat sayı olmalı."; return 0; }
        if (value <= 0) { error = "Fiyat sıfırdan büyük olmalı."; return 0; }
        if (value > 9_999_999_999m) { error = "Fiyat çok büyük."; return 0; }
        return decimal.Round(value, 2);
    }

    /// <summary>Sayı hücresini ya da metin olarak yazılmış sayıyı ("12,50" / "12.50" / "1.250,50") okur.</summary>
    private static bool TryDecimal(IXLCell cell, out decimal value)
    {
        value = 0;
        if (cell.Value.IsNumber) { value = (decimal)cell.Value.GetNumber(); return true; }
        var text = cell.GetString().Trim();
        if (text.Length == 0) return false;
        // Son ayırıcı ondalık, diğeri binlik sayılır.
        text = text.LastIndexOf(',') > text.LastIndexOf('.') ? text.Replace(".", "").Replace(',', '.') : text.Replace(",", "");
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
