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
    private static readonly string[] Headers = [MainIdHeader, NameHeader, BarcodeHeader, VariantHeader, StockHeader, PriceHeader];
    private static readonly string[] Optional = [MainIdHeader, VariantHeader];

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
        decimal[][] numbers = [[25, 199.90m], [18, 199.90m]];
        for (var r = 0; r < sample.Length; r++)
        {
            for (var c = 0; c < sample[r].Length; c++) ws.Cell(r + 2, c + 1).SetValue(sample[r][c]);
            ws.Cell(r + 2, 5).Value = numbers[r][0];
            ws.Cell(r + 2, 6).Value = numbers[r][1];
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private sealed record Row(int Number, string MainId, string Name, string Barcode, string? Variant, int Stock, decimal Price);

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

                var stock = ParseStock(ws.Cell(r, columns[StockHeader]), out var stockError);
                if (stockError is not null) problems.Add(stockError);
                var price = ParsePrice(ws.Cell(r, columns[PriceHeader]), out var priceError);
                if (priceError is not null) problems.Add(priceError);

                if (problems.Count > 0) { errors.Add(new ProductImportError(r, barcode, string.Join(" ", problems))); continue; }

                seen[barcode] = r;
                var variant = Text(VariantHeader);
                rows.Add(new Row(r, mainId, name, barcode, variant.Length > 0 ? variant : null, stock, price));
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

        int created = 0, updated = 0;
        foreach (var row in rows)
        {
            if (existing.TryGetValue(row.Barcode, out var product)) updated++;
            else { product = new Product { Barcode = row.Barcode }; db.Products.Add(product); created++; }
            product.MainId = row.MainId.Length > 0 ? row.MainId : null;
            product.Name = row.Name;
            product.Variant = row.Variant;
            product.Stock = row.Stock;
            product.Price = row.Price;
        }
        await db.SaveChangesAsync(ct);
        return (created, updated);
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
