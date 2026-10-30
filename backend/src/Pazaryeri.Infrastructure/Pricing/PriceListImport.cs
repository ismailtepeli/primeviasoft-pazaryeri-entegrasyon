using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Pricing;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Pricing;

public record PriceImportError(int Row, string Message);

public record PriceImportResult(int Created, int Updated, int Skipped, IReadOnlyList<PriceImportError> Errors);

public class PriceImportException(string message) : Exception(message);

/// <summary>
/// Fiyat listesine Excel'den toplu yükleme. Başlıklar: Barkod, Fiyat (zorunlu); Minimum, Koli (isteğe bağlı, ürüne yazılır).
/// Hatalı satırlar atlanır ve satır numarasıyla raporlanır; geçerli satırlar yine de kaydedilir.
/// </summary>
public class PriceListImportService(AppDbContext db)
{
    public const int MaxRows = 5000;

    public async Task<PriceImportResult> ImportAsync(Guid listId, Stream file, CancellationToken ct)
    {
        if (!await db.PriceLists.AnyAsync(l => l.Id == listId, ct)) throw new PriceImportException("Fiyat listesi bulunamadı.");
        XLWorkbook book;
        try { book = new XLWorkbook(file); }
        catch (Exception) { throw new PriceImportException("Dosya okunamadı; .xlsx biçiminde bir Excel dosyası yükleyin."); }

        using (book)
        {
            var sheet = book.Worksheets.FirstOrDefault() ?? throw new PriceImportException("Dosyada sayfa yok.");
            var used = sheet.RangeUsed() ?? throw new PriceImportException("Dosya boş.");
            var header = used.FirstRow().Cells().Select((c, i) => (Name: c.GetString().Trim().ToLowerInvariant(), Col: c.Address.ColumnNumber)).ToList();
            int? Col(params string[] names) => header.FirstOrDefault(h => names.Contains(h.Name)) is { Name.Length: > 0 } f ? f.Col : null;
            var barcodeCol = Col("barkod", "barcode");
            var priceCol = Col("fiyat", "price");
            if (barcodeCol is null || priceCol is null) throw new PriceImportException("İlk satırda 'Barkod' ve 'Fiyat' başlıkları olmalı.");
            var minCol = Col("minimum", "min");
            var packCol = Col("koli", "pack");

            var rows = used.RowsUsed().Skip(1).ToList();
            if (rows.Count > MaxRows) throw new PriceImportException($"En fazla {MaxRows} satır yüklenebilir.");

            var products = await db.Products.ToDictionaryAsync(p => p.Barcode, ct);
            var items = await db.PriceListItems.Where(i => i.PriceListId == listId).ToDictionaryAsync(i => i.ProductId, ct);
            var errors = new List<PriceImportError>();
            var seen = new HashSet<string>();
            int created = 0, updated = 0;

            foreach (var row in rows)
            {
                var n = row.RowNumber();
                var barcode = row.Cell(barcodeCol.Value).GetString().Trim();
                if (barcode.Length == 0) { errors.Add(new(n, "Barkod boş.")); continue; }
                if (!seen.Add(barcode)) { errors.Add(new(n, $"{barcode} dosyada birden fazla kez geçiyor; ilki kullanıldı.")); continue; }
                if (!products.TryGetValue(barcode, out var product)) { errors.Add(new(n, $"{barcode} barkodlu ürün yok.")); continue; }
                if (!row.Cell(priceCol.Value).TryGetValue<decimal>(out var price) || price < 0 || price > 100_000_000m)
                { errors.Add(new(n, $"{barcode}: fiyat geçerli bir sayı olmalı.")); continue; }

                var min = product.MinOrderQuantity; var pack = product.PackSize;
                if (minCol is { } mc && !row.Cell(mc).IsEmpty() && !row.Cell(mc).TryGetValue(out min)) { errors.Add(new(n, $"{barcode}: minimum adet geçersiz.")); continue; }
                if (packCol is { } pc && !row.Cell(pc).IsEmpty() && !row.Cell(pc).TryGetValue(out pack)) { errors.Add(new(n, $"{barcode}: koli adedi geçersiz.")); continue; }
                if (OrderQuantityRules.ValidateSettings(min, pack) is { } qe) { errors.Add(new(n, $"{barcode}: {qe}")); continue; }

                price = DealerPricing.Round(price);
                if (items.TryGetValue(product.Id, out var item)) { item.Price = price; updated++; }
                else { db.PriceListItems.Add(new PriceListItem { PriceListId = listId, ProductId = product.Id, Price = price }); created++; }
                product.MinOrderQuantity = min; product.PackSize = pack;
            }

            await db.SaveChangesAsync(ct);
            return new PriceImportResult(created, updated, errors.Count, errors);
        }
    }
}
