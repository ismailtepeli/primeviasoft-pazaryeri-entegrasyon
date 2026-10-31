namespace Pazaryeri.Application.Orders;

/// <summary>Hızlı siparişte bir satır; Row, kullanıcının gördüğü satır numarasıdır (yapıştırmada satır, Excel'de satır no).</summary>
public record QuickOrderRow(int Row, string Code, int Quantity);

public record QuickOrderRowError(int Row, string Code, string Message);

/// <summary>Metin ayrıştırma sonucu: geçerli satırlar ve sözdizimi hataları.</summary>
public record QuickOrderParse(IReadOnlyList<QuickOrderRow> Rows, IReadOnlyList<QuickOrderRowError> Errors);

/// <summary>
/// "kod;adet" listesi ayrıştırıcısı. Ayırıcı: noktalı virgül, sekme (Excel'den yapıştırma), virgül ya da boşluk ("KOD 12", "KOD x 12").
/// Başlık satırı ("Kod;Adet", "Barkod;Adet") ve boş satırlar atlanır; adet yazılmazsa 1 sayılır.
/// </summary>
public static class QuickOrderParser
{
    public const int MaxRows = 1000;
    private static readonly string[] HeaderWords = ["kod", "barkod", "code", "barcode", "ürün kodu", "urun kodu", "sku"];

    public static QuickOrderParse Parse(string? text)
    {
        var rows = new List<QuickOrderRow>();
        var errors = new List<QuickOrderRowError>();
        if (string.IsNullOrWhiteSpace(text)) return new(rows, errors);

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var n = i + 1;
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (rows.Count + errors.Count >= MaxRows) { errors.Add(new(n, "", $"En fazla {MaxRows} satır işlenir; kalanı atlandı.")); break; }

            var (code, qtyText) = Split(line);
            if (rows.Count == 0 && errors.Count == 0 && IsHeader(code)) continue;
            AddRow(rows, errors, n, code, qtyText);
        }
        return new(rows, errors);
    }

    /// <summary>Ortak satır doğrulaması; Excel okuyucu da bunu kullanır. Boş adet 1 sayılır.</summary>
    public static void AddRow(List<QuickOrderRow> rows, List<QuickOrderRowError> errors, int row, string code, string? qtyText)
    {
        code = code.Trim();
        if (code.Length == 0) { errors.Add(new(row, "", "Ürün kodu boş.")); return; }
        if (code.Length > 100) { errors.Add(new(row, code[..20] + "…", "Ürün kodu çok uzun.")); return; }
        var q = qtyText?.Trim() ?? "";
        if (q.Length == 0) { rows.Add(new(row, code, 1)); return; }
        if (!decimal.TryParse(q.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) || d != decimal.Truncate(d))
        { errors.Add(new(row, code, $"Adet '{q}' geçerli bir tam sayı değil.")); return; }
        if (d <= 0) { errors.Add(new(row, code, "Adet 0'dan büyük olmalı.")); return; }
        if (d > 100_000) { errors.Add(new(row, code, "Adet çok büyük.")); return; }
        rows.Add(new(row, code, (int)d));
    }

    public static bool IsHeader(string code) => HeaderWords.Contains(code.Trim().ToLowerInvariant());

    private static (string Code, string? Qty) Split(string line)
    {
        foreach (var sep in new[] { ';', '\t' })
        {
            var i = line.IndexOf(sep);
            if (i >= 0) return (line[..i], line[(i + 1)..].Split(sep)[0]);
        }
        var comma = line.IndexOf(',');
        if (comma >= 0) return (line[..comma], line[(comma + 1)..].Split(',')[0]);
        // "KOD 12" ya da "KOD x 12": yalnızca boşlukla ayrılmış son parça sayıysa adet sayılır (kodun içindeki x'e dokunulmaz).
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count >= 2 && decimal.TryParse(parts[^1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            var qty = parts[^1];
            parts.RemoveAt(parts.Count - 1);
            if (parts.Count >= 2 && parts[^1].Equals("x", StringComparison.OrdinalIgnoreCase)) parts.RemoveAt(parts.Count - 1);
            return (string.Join(' ', parts), qty);
        }
        return (line, null);
    }
}
