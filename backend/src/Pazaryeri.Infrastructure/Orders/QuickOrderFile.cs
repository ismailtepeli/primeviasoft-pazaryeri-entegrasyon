using ClosedXML.Excel;
using Pazaryeri.Application.Orders;

namespace Pazaryeri.Infrastructure.Orders;

/// <summary>Hızlı sipariş Excel dosyası okuyucu: ilk sayfa, A sütunu kod, B sütunu adet (boşsa 1). Başlık satırı atlanır.</summary>
public static class QuickOrderFile
{
    public static QuickOrderParse ReadXlsx(Stream file)
    {
        XLWorkbook book;
        try { book = new XLWorkbook(file); }
        catch (Exception) { throw new StoreException(400, "Dosya okunamadı; .xlsx biçiminde bir Excel dosyası ya da .csv yükleyin."); }
        using (book)
        {
            var sheet = book.Worksheets.FirstOrDefault() ?? throw new StoreException(400, "Dosyada sayfa yok.");
            var used = sheet.RangeUsed() ?? throw new StoreException(400, "Dosya boş.");
            var rows = new List<QuickOrderRow>();
            var errors = new List<QuickOrderRowError>();
            var first = true;
            foreach (var r in used.RowsUsed())
            {
                var n = r.RowNumber();
                var code = r.Cell(1).GetFormattedString().Trim();
                var qty = r.Cell(2).GetFormattedString();
                if (first) { first = false; if (QuickOrderParser.IsHeader(code)) continue; }
                if (code.Length == 0 && qty.Length == 0) continue;
                if (rows.Count + errors.Count >= QuickOrderParser.MaxRows) { errors.Add(new(n, "", $"En fazla {QuickOrderParser.MaxRows} satır işlenir; kalanı atlandı.")); break; }
                QuickOrderParser.AddRow(rows, errors, n, code, qty);
            }
            return new QuickOrderParse(rows, errors);
        }
    }
}
