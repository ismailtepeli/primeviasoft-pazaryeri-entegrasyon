using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Products;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Products;

/// <summary>
/// Ürün fiyatı güncelleme. Fiyat yalnızca ürünün satış fiyatını değiştirir; kanallara bir sonraki stok-fiyat senkronunda yansır.
/// Eşzamanlı düzenleme (satır sürümü) çakışırsa ürünler yeniden okunup yeniden denenir.
/// </summary>
public class ProductPriceService(AppDbContext db) : IProductPriceService
{
    public const decimal MaxPrice = 9_999_999_999m;
    private const string BarcodeHeader = "Barkod";
    private const string NameHeader = "Ürün Adı";
    private const string PriceHeader = "Fiyat";
    private const int Attempts = 3;

    private static decimal Round(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    private static string? ValidatePrice(decimal price) =>
        price <= 0 ? "Fiyat sıfırdan büyük olmalı." : price > MaxPrice ? "Fiyat çok büyük." : null;

    public async Task<ProductPriceDto> SetPriceAsync(Guid productId, decimal price, CancellationToken ct = default)
    {
        if (ValidatePrice(price) is { } error) throw new ProductPriceException(400, error);
        var rounded = Round(price);
        if (rounded <= 0) throw new ProductPriceException(400, "Fiyat sıfırdan büyük olmalı.");
        for (var attempt = 1; ; attempt++)
        {
            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct) ?? throw new ProductPriceException(404, "Ürün bulunamadı.");
            product.Price = rounded;
            try { await db.SaveChangesAsync(ct); return new ProductPriceDto(product.Id, product.Price); }
            catch (DbUpdateConcurrencyException) when (attempt < Attempts) { db.ChangeTracker.Clear(); }
        }
    }

    /// <summary>Yeni fiyat; sonuç geçersizse (sıfır/negatif/çok büyük) null.</summary>
    public static decimal? Apply(string mode, decimal value, decimal current)
    {
        var next = mode switch
        {
            PriceChangeModes.Percent => Round(current * (1 + value / 100m)),
            PriceChangeModes.Amount => Round(current + value),
            _ => Round(value),
        };
        return next > 0 && next <= MaxPrice ? next : null;
    }

    public async Task<PriceChangeResult> BulkChangeAsync(BulkPriceInput input, CancellationToken ct = default)
    {
        var ids = input.ProductIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) throw new ProductPriceException(400, "En az bir ürün seçin.");
        if (ids.Count > IProductPriceService.MaxBulk) throw new ProductPriceException(400, $"Tek seferde en fazla {IProductPriceService.MaxBulk} ürün değiştirilebilir.");
        var mode = input.Mode?.Trim().ToLowerInvariant();
        if (mode is not (PriceChangeModes.Percent or PriceChangeModes.Amount or PriceChangeModes.Set)) throw new ProductPriceException(400, "Değişiklik türü geçersiz.");
        if (mode == PriceChangeModes.Set && ValidatePrice(input.Value) is { } setError) throw new ProductPriceException(400, setError);
        if (mode == PriceChangeModes.Percent && (input.Value < -90m || input.Value > 1000m)) throw new ProductPriceException(400, "Yüzde -90 ile 1000 arasında olmalı.");
        if (mode == PriceChangeModes.Amount && Math.Abs(input.Value) > MaxPrice) throw new ProductPriceException(400, "Tutar çok büyük.");

        for (var attempt = 1; ; attempt++)
        {
            var products = await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
            int updated = 0, unchanged = 0;
            var skipped = new List<PriceChangeSkipped>();
            foreach (var p in products.OrderBy(p => p.Name))
            {
                if (Apply(mode, input.Value, p.Price) is not { } next) { skipped.Add(new PriceChangeSkipped(p.Name, "Yeni fiyat sıfırdan büyük olmalı.")); continue; }
                if (next == p.Price) { unchanged++; continue; }
                p.Price = next;
                updated++;
            }
            try { await db.SaveChangesAsync(ct); return new PriceChangeResult(updated, unchanged, skipped); }
            catch (DbUpdateConcurrencyException) when (attempt < Attempts) { db.ChangeTracker.Clear(); }
        }
    }

    public async Task<byte[]> BuildSheetAsync(CancellationToken ct = default)
    {
        var rows = await db.Products.AsNoTracking().OrderBy(p => p.Name).ThenBy(p => p.Id).Take(IProductPriceService.MaxSheetRows)
            .Select(p => new { p.Barcode, p.Name, p.Price }).ToListAsync(ct);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Fiyatlar");
        string[] headers = [BarcodeHeader, NameHeader, PriceHeader];
        for (var i = 0; i < headers.Length; i++) { var c = ws.Cell(1, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true; }
        ws.Column(1).Style.NumberFormat.Format = "@"; // barkod metin kalsın, başındaki sıfırlar silinmesin
        for (var r = 0; r < rows.Count; r++)
        {
            ws.Cell(r + 2, 1).SetValue(rows[r].Barcode);
            ws.Cell(r + 2, 2).SetValue(rows[r].Name);
            ws.Cell(r + 2, 3).Value = rows[r].Price;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<PriceImportResult> ImportAsync(Stream xlsx, CancellationToken ct = default)
    {
        XLWorkbook wb;
        try { wb = new XLWorkbook(xlsx); }
        catch (Exception) { throw new ProductPriceException(400, "Dosya okunamadı. Lütfen indirdiğiniz .xlsx dosyasını kullanın."); }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault() ?? throw new ProductPriceException(400, "Dosyada sayfa bulunamadı.");
            var used = ws.RangeUsed() ?? throw new ProductPriceException(400, "Dosya boş.");
            int? barcodeCol = null, priceCol = null;
            for (var c = 1; c <= used.LastColumn().ColumnNumber(); c++)
            {
                var text = ws.Cell(1, c).GetString().Trim();
                if (string.Equals(text, BarcodeHeader, StringComparison.OrdinalIgnoreCase)) barcodeCol ??= c;
                else if (string.Equals(text, PriceHeader, StringComparison.OrdinalIgnoreCase)) priceCol ??= c;
            }
            if (barcodeCol is null || priceCol is null) throw new ProductPriceException(400, $"'{BarcodeHeader}' ve '{PriceHeader}' başlıkları olmalı.");
            var last = used.LastRow().RowNumber();
            if (last - 1 > IProductPriceService.MaxSheetRows) throw new ProductPriceException(400, $"Tek seferde en fazla {IProductPriceService.MaxSheetRows} satır yüklenebilir.");

            var errors = new List<PriceImportError>();
            var wanted = new Dictionary<string, (int Row, decimal Price)>(StringComparer.OrdinalIgnoreCase);
            for (var r = 2; r <= last; r++)
            {
                var barcodeCell = ws.Cell(r, barcodeCol.Value);
                var priceCell = ws.Cell(r, priceCol.Value);
                if (barcodeCell.IsEmpty() && priceCell.IsEmpty()) continue;
                var barcode = barcodeCell.GetString().Trim();
                if (barcode.Length == 0) { errors.Add(new PriceImportError(r, "", "Barkod boş.")); continue; }
                if (wanted.TryGetValue(barcode, out var first)) { errors.Add(new PriceImportError(r, barcode, $"Barkod dosyada {first.Row}. satırda da var.")); continue; }
                var price = ProductImportService.ParsePrice(priceCell, out var error);
                if (error is not null) { errors.Add(new PriceImportError(r, barcode, error)); continue; }
                wanted[barcode] = (r, price);
            }

            for (var attempt = 1; ; attempt++)
            {
                var keys = wanted.Keys.ToList();
                var products = (await db.Products.Where(p => keys.Contains(p.Barcode)).ToListAsync(ct)).ToDictionary(p => p.Barcode, StringComparer.OrdinalIgnoreCase);
                int updated = 0, unchanged = 0;
                var notFound = new List<PriceImportError>();
                foreach (var (barcode, want) in wanted)
                {
                    if (!products.TryGetValue(barcode, out var p)) { notFound.Add(new PriceImportError(want.Row, barcode, "Bu barkodla kayıtlı ürün yok.")); continue; }
                    if (p.Price == want.Price) { unchanged++; continue; }
                    p.Price = want.Price;
                    updated++;
                }
                try
                {
                    await db.SaveChangesAsync(ct);
                    return new PriceImportResult(updated, unchanged, errors.Concat(notFound).OrderBy(e => e.Row).ToList());
                }
                catch (DbUpdateConcurrencyException) when (attempt < Attempts) { db.ChangeTracker.Clear(); }
            }
        }
    }
}
