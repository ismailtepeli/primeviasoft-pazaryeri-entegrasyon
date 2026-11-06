using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Pazaryeri.Application.Feeds;

namespace Pazaryeri.Infrastructure.Feeds;

/// <summary>Bir XML ürün öğesinin yaprak alanları: göreli yol → değerler (görsel listesi gibi tekrarlananlar birden çok değer).</summary>
public sealed class FeedItem
{
    public Dictionary<string, List<string>> Values { get; } = new(StringComparer.Ordinal);
    public void Add(string path, string value)
    {
        if (!Values.TryGetValue(path, out var list)) Values[path] = list = [];
        list.Add(value);
    }
}

/// <summary>
/// Tedarikçi XML'ini tanır: ürün öğesini (tekrarlanan, çok alanlı öğe) bulur, alan adlarını eş anlamlı sözlükle sistem rollerine eşler
/// ve öğeleri akış olarak okur. XML güvenlidir: DTD yok sayılır, dış varlık çözülmez. Biçim önceden bilinmek zorunda değildir; Ticimax/T-Soft/İdeasoft
/// tarzı Türkçe alan adları, Google Merchant (RSS, g:) ve genel İngilizce alan adları tanınır.
/// </summary>
public static class XmlFeed
{
    public const int MaxDepth = 32;
    public const int MaxPaths = 5000;
    public const int MaxItems = 50_000;
    public const int MaxImages = 8;

    static XmlFeed() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // windows-1254 gibi eski kodlamalar

    // ---------- okuma ----------

    private static readonly Regex DeclaredEncoding = new(@"<\?xml[^>]*?encoding\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Kodlamayı BOM'dan, yoksa XML bildiriminden (encoding="windows-1254") anlar; bulamazsa UTF-8.</summary>
    private static Encoding DetectEncoding(byte[] head, int n)
    {
        if (n >= 2 && head[0] == '<' && head[1] == 0) return Encoding.Unicode; // BOM'suz UTF-16
        if (n >= 2 && head[0] == 0 && head[1] == '<') return Encoding.BigEndianUnicode;
        var m = DeclaredEncoding.Match(Encoding.Latin1.GetString(head, 0, n));
        if (!m.Success) return Encoding.UTF8;
        try
        {
            var enc = Encoding.GetEncoding(m.Groups[1].Value);
            return enc.CodePage is 1200 or 1201 or 12000 or 12001 ? Encoding.UTF8 : enc; // bayt örüntüsü UTF-16 değilse bildirime güvenme
        }
        catch (ArgumentException) { return Encoding.UTF8; }
    }

    /// <summary>
    /// Okuyucuyu açar (akış aranabilir olmalı). Metin, <see cref="XmlRepairReader"/> ile onarılarak okunur: başta artık metin, kaçışsız &amp; ve
    /// benzeri yaygın bozukluklar dosyayı reddettirmez. DTD yok sayılır, dış varlık çözülmez.
    /// </summary>
    public static XmlReader Open(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        var head = new byte[1024];
        var n = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Seek(0, SeekOrigin.Begin);
        var text = new StreamReader(stream, DetectEncoding(head, n), detectEncodingFromByteOrderMarks: true, bufferSize: 16384, leaveOpen: true);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, CheckCharacters = false, IgnoreComments = true, IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true, MaxCharactersFromEntities = 1024,
        };
        return XmlReader.Create(new XmlRepairReader(text), settings);
    }

    private static FeedException NotXml(Exception? inner = null) =>
        new(422, inner is XmlException ? "Adres geçerli bir XML döndürmüyor (ya da dosya bozuk). Tarayıcıda açıp XML olduğundan emin olun." : "Adres okunamadı.");

    private sealed class Stat
    {
        public int Count;
        public bool HasChildren;
        /// <summary>Öğenin kendi öznitelikleri var (alt öğesi olmasa da ürün satırı olabilir: &lt;Product Barcode="1" /&gt;).</summary>
        public bool HasAttributes;
        public readonly List<string> Samples = [];
        public int Filled;
    }

    private sealed record Scan(string Root, string? RootNamespace, Dictionary<string, Stat> Stats);

    private static Scan ScanStructure(Stream stream)
    {
        var stats = new Dictionary<string, Stat>(StringComparer.Ordinal);
        var names = new List<string>();
        var text = new StringBuilder();
        string? root = null, rootNs = null;
        try
        {
            using var reader = Open(stream);
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        if (names.Count == 0) { root = reader.LocalName; rootNs = reader.NamespaceURI; }
                        names.Add(reader.LocalName);
                        if (names.Count > MaxDepth) throw new FeedException(422, "XML çok derin iç içe; desteklenmiyor.");
                        var path = string.Join('/', names);
                        if (names.Count > 1) Get(stats, string.Join('/', names.Take(names.Count - 1))).HasChildren = true;
                        var stat = Get(stats, path);
                        stat.Count++;
                        text.Clear();
                        if (reader.HasAttributes)
                        {
                            var empty = reader.IsEmptyElement;
                            stat.HasAttributes = true;
                            while (reader.MoveToNextAttribute())
                            {
                                if (reader.Prefix is "xmlns" or "xml" || reader.LocalName == "xmlns") continue;
                                var a = Get(stats, path + "/@" + reader.LocalName);
                                a.Count++;
                                AddSample(a, reader.Value);
                            }
                            reader.MoveToElement();
                            if (empty) names.RemoveAt(names.Count - 1);
                        }
                        else if (reader.IsEmptyElement) names.RemoveAt(names.Count - 1);
                        if (stats.Count > MaxPaths) throw new FeedException(422, "XML yapısı çok dağınık; ürün listesi tanınamadı.");
                        break;
                    case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                        text.Append(reader.Value);
                        break;
                    case XmlNodeType.EndElement:
                        if (names.Count == 0) break;
                        AddSample(stats[string.Join('/', names)], text.ToString());
                        text.Clear();
                        names.RemoveAt(names.Count - 1);
                        break;
                }
            }
        }
        catch (XmlException e) { throw NotXml(e); }
        if (root is null) throw new FeedException(422, "Adres boş ya da XML değil.");
        if (root.Equals("html", StringComparison.OrdinalIgnoreCase)) throw new FeedException(422, "Adres bir web sayfası döndürüyor, XML değil. Doğrudan XML bağlantısını girin.");
        return new Scan(root, rootNs, stats);
    }

    private static Stat Get(Dictionary<string, Stat> stats, string path)
    {
        if (!stats.TryGetValue(path, out var s)) stats[path] = s = new Stat();
        return s;
    }

    private static void AddSample(Stat s, string value)
    {
        var v = value.Trim();
        if (v.Length == 0) return;
        s.Filled++;
        if (s.Samples.Count < 3 && !s.Samples.Contains(v)) s.Samples.Add(v.Length > 120 ? v[..120] + "…" : v);
    }

    // ---------- öğeleri akış olarak oku ----------

    /// <summary>Verilen yoldaki her ürün öğesinin yaprak alanlarını sırayla verir.</summary>
    public static IEnumerable<FeedItem> ReadItems(Stream stream, string itemPath, int limit = MaxItems)
    {
        XmlReader reader;
        try { reader = Open(stream); }
        catch (XmlException e) { throw NotXml(e); }
        using (reader)
        {
            var names = new List<string>();
            var text = new StringBuilder();
            FeedItem? current = null;
            var itemDepth = 0;
            var produced = 0;
            while (true)
            {
                bool more;
                try { more = reader.Read(); }
                catch (XmlException e) { throw NotXml(e); }
                if (!more) yield break;

                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        names.Add(reader.LocalName);
                        var isEmpty = reader.IsEmptyElement;
                        if (current is null)
                        {
                            if (names.Count <= MaxDepth && string.Join('/', names) == itemPath) { current = new FeedItem(); itemDepth = names.Count; AddAttributes(reader, current, ""); }
                        }
                        else
                        {
                            var rel = string.Join('/', names.Skip(itemDepth));
                            AddAttributes(reader, current, rel);
                            text.Clear();
                        }
                        if (isEmpty)
                        {
                            if (current is not null && names.Count == itemDepth)
                            {
                                yield return current; current = null;
                                if (++produced >= limit) yield break;
                            }
                            names.RemoveAt(names.Count - 1);
                        }
                        break;
                    case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                        if (current is not null) text.Append(reader.Value);
                        break;
                    case XmlNodeType.EndElement:
                        if (names.Count == 0) break;
                        if (current is not null)
                        {
                            if (names.Count == itemDepth)
                            {
                                yield return current; current = null;
                                if (++produced >= limit) yield break;
                            }
                            else if (names.Count > itemDepth)
                            {
                                var value = text.ToString().Trim();
                                if (value.Length > 0) current.Add(string.Join('/', names.Skip(itemDepth)), value);
                            }
                            text.Clear();
                        }
                        names.RemoveAt(names.Count - 1);
                        break;
                }
            }
        }
    }

    private static void AddAttributes(XmlReader reader, FeedItem item, string rel)
    {
        if (!reader.HasAttributes) return;
        while (reader.MoveToNextAttribute())
        {
            if (reader.Prefix is "xmlns" or "xml" || reader.LocalName == "xmlns") continue;
            var v = reader.Value.Trim();
            if (v.Length > 0) item.Add(rel.Length == 0 ? "@" + reader.LocalName : rel + "/@" + reader.LocalName, v);
        }
        reader.MoveToElement();
    }

    // ---------- alan adı tanıma ----------

    /// <summary>Küçük harf, Türkçe karakterler sadeleştirilmiş, yalnızca harf/rakam.</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            var c = ch switch { 'ı' or 'İ' or 'I' => 'i', 'ş' or 'Ş' => 's', 'ğ' or 'Ğ' => 'g', 'ü' or 'Ü' => 'u', 'ö' or 'Ö' => 'o', 'ç' or 'Ç' => 'c', _ => char.ToLowerInvariant(ch) };
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    // Öncelik sırasıyla eş anlamlılar: önce gelen daha çok tercih edilir.
    private static readonly Dictionary<string, string[]> Synonyms = new()
    {
        [FeedRoles.Barcode] = ["barkod", "barcode", "barkodno", "urunbarkodu", "productbarcode", "ean", "ean13", "gtin", "gtin13", "upc",
            "sku", "stokkodu", "stockcode", "stokkod", "urunkodu", "productcode", "itemcode", "urunno", "kod", "code", "varyasyonkodu", "variantcode",
            "id", "productid", "urunid", "stokid", "uniqueid"],
        [FeedRoles.Name] = ["urunadi", "urunismi", "adi", "ad", "isim", "name", "title", "productname", "baslik", "urunbasligi", "stokadi", "urunaciklamakisa"],
        // Tedarikçinin bayiye sattığı fiyat varsa önce o; sonra satış fiyatları.
        [FeedRoles.Price] = ["bayifiyati", "bayialisfiyati", "alisfiyati", "alisfiyat", "dealerprice", "wholesaleprice", "wholesale", "cost", "maliyet", "toptanfiyat", "toptanfiyati", "toptansatisfiyati", "ozelfiyat",
            "saleprice", "indirimlifiyat", "indirimlifiyati", "specialprice", "satisfiyati", "satisfiyat", "price", "fiyat", "urunfiyati", "listprice", "listefiyati", "piyasafiyati", "kdvlifiyat", "kdvlisatisfiyati"],
        [FeedRoles.Stock] = ["stok", "stokadedi", "stokmiktari", "stockquantity", "quantity", "stock", "miktar", "adet", "qty", "stocklevel", "inventory", "inventoryquantity", "kalanstok", "stokdurumu", "availability", "instock", "mevcut"],
        [FeedRoles.Brand] = ["marka", "markaadi", "brand", "brandname", "uretici", "manufacturer"],
        [FeedRoles.Category] = ["kategoriyolu", "categorypath", "kategori", "category", "kategoriadi", "categoryname", "productcategory", "producttype", "googleproductcategory", "anakategori", "kategoriler", "categories"],
        [FeedRoles.Description] = ["aciklama", "urunaciklamasi", "detay", "description", "longdescription", "detayaciklama", "icerik", "content"],
        [FeedRoles.Vat] = ["kdv", "kdvorani", "kdvoran", "vat", "vatrate", "taxrate", "tax", "kdvyuzde"],
        [FeedRoles.MainId] = ["urunkartiid", "urunkartikodu", "anakod", "anaurunkodu", "grupkodu", "groupid", "itemgroupid", "parentid", "productmainid", "mainid", "modelkodu", "model", "mpn"],
    };

    private static readonly Regex ImageName = new(@"^(resim|image|img|foto|fotograf|picture|pic|gorsel|photo)s?(url|link|path|src)?(ek|extra)?\d*$|^additionalimage(link|url)?\d*$|^(resim|image)(url|link)s?$", RegexOptions.Compiled);
    private static readonly string[] ImageBlock = ["thumb", "small", "icon", "logo", "alt", "title", "caption", "count", "sayisi"];

    private sealed record Field(string Rel, string Last, string Combined, Stat Stat, double PerItem);

    private static IReadOnlyList<Field> FieldsOf(Dictionary<string, Stat> stats, string itemPath, int itemCount)
    {
        var prefix = itemPath + "/";
        var list = new List<Field>();
        foreach (var (key, stat) in stats)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || stat.HasChildren || stat.Filled == 0) continue;
            var rel = key[prefix.Length..];
            var segs = rel.Split('/');
            var last = Normalize(segs[^1]);
            var parent = segs.Length > 1 ? Normalize(segs[^2]) : "";
            list.Add(new Field(rel, last, parent + last, stat, stat.Count / (double)Math.Max(itemCount, 1)));
        }
        return list;
    }

    private static bool LooksNumeric(Stat s) => s.Samples.Count > 0 && s.Samples.All(v => TryParseNumber(v, out _));
    private static bool LooksStock(Stat s) => s.Samples.Count > 0 && s.Samples.All(v => TryParseNumber(v, out _) || StockWord(v) is not null);
    private static bool LooksUrl(Stat s) => s.Samples.Any(v => v.StartsWith("http", StringComparison.OrdinalIgnoreCase) || v.StartsWith("//", StringComparison.Ordinal));

    /// <summary>Alan öğelerin en az bu kadarında dolu değilse (ör. yalnızca bazı ürünlerde olan sale_price, gtin) rolün asıl alanı sayılmaz.</summary>
    private const double MinCoverage = 0.9;

    private static Dictionary<string, List<string>> DetectMapping(IReadOnlyList<Field> fields, int itemCount)
    {
        var mapping = new Dictionary<string, List<string>>();
        var used = new HashSet<string>();
        foreach (var role in new[] { FeedRoles.Barcode, FeedRoles.Name, FeedRoles.Price, FeedRoles.Stock, FeedRoles.Brand, FeedRoles.Category, FeedRoles.Description, FeedRoles.Vat, FeedRoles.MainId })
        {
            // Eş anlamlıları öncelik sırasıyla gez; önce yeterince dolu (≥ %90) ilk alanı seç, hiçbiri o kadar dolu değilse en dolu olanı al.
            // Aynı adlı alanlarda üst düzeydeki (ör. price) iç içe olana (shipping/price) tercih edilir.
            var hits = new List<Field>();
            foreach (var syn in Synonyms[role])
            {
                var hit = fields.Where(f => !used.Contains(f.Rel) && !hits.Contains(f) && (f.Last == syn || f.Combined == syn))
                    .Where(f => role switch { FeedRoles.Price or FeedRoles.Vat => LooksNumeric(f.Stat), FeedRoles.Stock => LooksStock(f.Stat), _ => true })
                    .OrderByDescending(f => f.Last == syn).ThenBy(f => f.Rel.Count(ch => ch == '/')).ThenByDescending(f => f.Stat.Filled).FirstOrDefault();
                if (hit is not null) hits.Add(hit);
            }
            if (hits.Count == 0) continue;
            var best = hits.FirstOrDefault(f => f.Stat.Filled >= MinCoverage * itemCount) ?? hits.OrderByDescending(f => f.Stat.Filled).First();
            mapping[role] = [best.Rel];
            used.Add(best.Rel);
        }
        var images = fields.Where(f => !used.Contains(f.Rel) && LooksUrl(f.Stat) && (ImageName.IsMatch(f.Last) || ImageName.IsMatch(f.Combined)) && !ImageBlock.Any(b => f.Last.Contains(b)))
            .OrderBy(f => f.Rel, StringComparer.Ordinal).Select(f => f.Rel).ToList();
        if (images.Count > 0) mapping[FeedRoles.Images] = images;
        // Ana kod barkodla aynı alanı göstermesin.
        if (mapping.TryGetValue(FeedRoles.MainId, out var m) && mapping.TryGetValue(FeedRoles.Barcode, out var b) && m[0] == b[0]) mapping.Remove(FeedRoles.MainId);
        return mapping;
    }

    private static int ScoreCandidate(Dictionary<string, List<string>> mapping, IReadOnlyList<Field> fields, int count)
    {
        var score = 0;
        foreach (var role in FeedRoles.Required) if (mapping.ContainsKey(role)) score += 100;
        foreach (var role in FeedRoles.All.Except(FeedRoles.Required)) if (mapping.ContainsKey(role)) score += 10;
        score += (int)Math.Min(count, 1000) / 100;
        // Ürün başına birden çok barkod/ad geliyorsa bu bir varyant listesi içeren üst öğedir; satır olarak uygun değil.
        foreach (var role in new[] { FeedRoles.Barcode, FeedRoles.Name })
            if (mapping.TryGetValue(role, out var paths) && fields.FirstOrDefault(f => f.Rel == paths[0]) is { PerItem: > 1.2 }) score -= 150;
        return score;
    }

    private static string FormatName(Scan scan, string itemPath)
    {
        if (scan.Root.Equals("rss", StringComparison.OrdinalIgnoreCase) && itemPath.EndsWith("/item", StringComparison.Ordinal)) return "RSS / Google Merchant";
        if (scan.Root.Equals("feed", StringComparison.OrdinalIgnoreCase) && itemPath.EndsWith("/entry", StringComparison.Ordinal)) return "Atom";
        return "Genel XML (" + itemPath.Replace("/", " › ") + ")";
    }

    /// <summary>XML'i tarar: ürün öğesi adaylarını puanlar, en iyisini ve alan eşlemesini önerir. <paramref name="forcedItemPath"/> verilirse yalnızca o öğe kullanılır.</summary>
    public static FeedAnalysis Analyze(Stream stream, string? forcedItemPath, Dictionary<string, List<string>>? forcedMapping, FeedOptions options)
    {
        var scan = ScanStructure(stream);
        var candidates = new List<(string Path, int Count, int Score, IReadOnlyList<Field> Fields, Dictionary<string, List<string>> Mapping)>();
        foreach (var (path, stat) in scan.Stats)
        {
            if (!(stat.HasChildren || stat.HasAttributes) || path.Contains("/@", StringComparison.Ordinal) || !path.Contains('/')) continue; // kök öğe ürün olamaz
            var fields = FieldsOf(scan.Stats, path, stat.Count);
            if (fields.Count < 3) continue;
            var mapping = DetectMapping(fields, stat.Count);
            if (mapping.Count == 0) continue;
            candidates.Add((path, stat.Count, ScoreCandidate(mapping, fields, stat.Count), fields, mapping));
        }
        if (candidates.Count == 0) throw new FeedException(422, "XML'de ürün listesi bulunamadı. Barkod, ad ve fiyat alanları olan tekrarlanan bir ürün öğesi bekleniyor.");

        var ordered = candidates.OrderByDescending(c => c.Score).ThenByDescending(c => c.Count).ThenBy(c => c.Path.Length).ToList();
        var chosen = forcedItemPath is null ? ordered[0] : ordered.FirstOrDefault(c => c.Path == forcedItemPath);
        if (chosen.Path is null) throw new FeedException(400, "Seçilen ürün öğesi XML'de bulunamadı.");

        var mappingFinal = forcedMapping is { Count: > 0 } ? Sanitize(forcedMapping, chosen.Fields) : chosen.Mapping;
        var missing = FeedRoles.Required.Where(r => !mappingFinal.ContainsKey(r)).ToList();

        var fieldInfos = chosen.Fields.OrderBy(f => f.Rel, StringComparer.Ordinal).Select(f => new FeedFieldInfo(f.Rel, f.Stat.Samples, f.PerItem > 1.2)).ToList();
        var preview = new List<FeedPreviewRow>();
        if (missing.Count == 0)
        {
            foreach (var item in ReadItems(stream, chosen.Path, 10))
            {
                var (row, problems) = Map(item, mappingFinal, options);
                preview.Add(new FeedPreviewRow(row?.Barcode, row?.Name, row?.Price, row?.Stock, row?.Brand, row?.Category, row?.Images.Count ?? 0, problems));
            }
        }
        return new FeedAnalysis(FormatName(scan, chosen.Path), chosen.Path, chosen.Count,
            ordered.Take(6).Select(c => new FeedCandidate(c.Path, c.Count, c.Score)).ToList(), fieldInfos,
            mappingFinal.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value), missing, preview);
    }

    /// <summary>Kullanıcının elle seçtiği eşlemeyi doğrular: yalnızca bilinen roller ve öğede var olan yollar.</summary>
    private static Dictionary<string, List<string>> Sanitize(Dictionary<string, List<string>> mapping, IReadOnlyList<Field> fields)
    {
        var known = fields.Select(f => f.Rel).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, List<string>>();
        foreach (var (role, paths) in mapping)
        {
            if (!FeedRoles.All.Contains(role)) continue;
            var valid = paths.Where(known.Contains).Distinct().ToList();
            if (!FeedRoles.MultiPath.Contains(role)) valid = valid.Take(1).ToList();
            if (valid.Count > 0) result[role] = valid;
        }
        return result;
    }

    // ---------- değer dönüştürme ----------

    private static string? First(FeedItem item, Dictionary<string, List<string>> mapping, string role) =>
        mapping.TryGetValue(role, out var paths) ? paths.SelectMany(p => item.Values.GetValueOrDefault(p) ?? []).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() : null;

    /// <summary>"1.234,56", "1234.56", "TRY 12,50", "₺99" gibi yazılmış sayıyı okur. Tek nokta ondalık; tek virgülden sonra 3 hane binliktir.</summary>
    public static bool TryParseNumber(string text, out decimal value)
    {
        value = 0;
        var t = new string(text.Where(c => char.IsAsciiDigit(c) || c is '.' or ',' or '-').ToArray()).Trim('.', ',');
        if (t.Length == 0 || !t.Any(char.IsAsciiDigit)) return false;
        var comma = t.LastIndexOf(',');
        var dot = t.LastIndexOf('.');
        if (comma >= 0 && dot >= 0) t = comma > dot ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
        else if (comma >= 0) t = t.Count(c => c == ',') == 1 && t.Length - comma - 1 <= 2 ? t.Replace(',', '.') : t.Replace(",", "");
        else if (dot >= 0 && t.Count(c => c == '.') > 1) t = t.Replace(".", "");
        return decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static readonly string[] InStockWords = ["instock", "in stock", "stokta", "stoktavar", "var", "mevcut", "true", "evet", "yes", "available", "1"];
    private static readonly string[] OutStockWords = ["outofstock", "out of stock", "stoktayok", "stokyok", "yok", "tukendi", "false", "hayir", "no", "unavailable", "preorder", "0"];

    /// <summary>"in stock / stokta var" gibi metin durumlarını okur: true = var, false = yok, null = tanınmadı.</summary>
    public static bool? StockWord(string text)
    {
        var n = text.Trim().ToLowerInvariant().Replace('ı', 'i').Replace('ş', 's').Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c').Replace('ğ', 'g');
        if (InStockWords.Contains(n) || InStockWords.Contains(n.Replace(" ", ""))) return true;
        if (OutStockWords.Contains(n) || OutStockWords.Contains(n.Replace(" ", ""))) return false;
        return null;
    }

    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Blocks = new("<(script|style)[^>]*>.*?</\\1>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex Breaks = new("<\\s*(br|/p|/div|/li|/h[1-6])\\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>HTML açıklamayı düz metne çevirir (etiketler atılır, satır sonları korunur).</summary>
    public static string PlainText(string html)
    {
        var s = Blocks.Replace(html, " ");
        s = Breaks.Replace(s, "\n");
        s = Tags.Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t ]+", " ");
        s = Regex.Replace(s, @"\s*\n\s*", "\n");
        return s.Trim();
    }

    /// <summary>"Giyim / Tişört", "Giyim | Tişört" gibi yolları "Giyim > Tişört" biçimine getirir; en çok 5 seviye.</summary>
    public static string? NormalizeCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Contains('>') ? raw.Split('>') : raw.Split(['/', '|', '»', '\\', ';'], StringSplitOptions.None);
        var clean = parts.Select(p => WebUtility.HtmlDecode(p).Trim()).Where(p => p.Length > 0).Select(p => p.Length > 200 ? p[..200] : p).Take(5).ToList();
        return clean.Count == 0 ? null : string.Join(" > ", clean);
    }

    /// <summary>Ham öğeyi eşlemeye göre normalleştirir. Zorunlu alan eksik/geçersizse satır null döner ve nedenleri listelenir.</summary>
    public static (FeedRow? Row, IReadOnlyList<string> Problems) Map(FeedItem item, Dictionary<string, List<string>> mapping, FeedOptions options)
    {
        var problems = new List<string>();
        var barcode = First(item, mapping, FeedRoles.Barcode);
        if (barcode is null) problems.Add("Barkod/ürün kodu boş.");
        else if (barcode.Length > 100) problems.Add("Barkod 100 karakteri aşıyor.");
        var name = First(item, mapping, FeedRoles.Name);
        if (name is null) problems.Add("Ürün adı boş.");
        else { name = PlainText(name); if (name.Length > 500) name = name[..500]; }
        var priceText = First(item, mapping, FeedRoles.Price);
        decimal price = 0;
        if (priceText is null || !TryParseNumber(priceText, out price) || price <= 0 || price > 9_999_999_999m) problems.Add("Fiyat geçersiz.");

        int? stock = null;
        bool? inStock = null;
        if (First(item, mapping, FeedRoles.Stock) is { } stockText)
        {
            if (TryParseNumber(stockText, out var s) && s >= 0) stock = (int)Math.Min(decimal.Truncate(s), int.MaxValue);
            else inStock = StockWord(stockText);
        }

        var vatText = First(item, mapping, FeedRoles.Vat);
        int? vat = null;
        if (vatText is not null && TryParseNumber(vatText, out var v)) { if (v > 0 && v < 1) v *= 100; if (v is >= 0 and <= 100) vat = (int)decimal.Round(v); }

        var images = new List<string>();
        if (mapping.TryGetValue(FeedRoles.Images, out var imagePaths))
            foreach (var raw in imagePaths.SelectMany(p => item.Values.GetValueOrDefault(p) ?? []))
                foreach (var piece in raw.Split([',', ';', '|', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var url = piece.StartsWith("//", StringComparison.Ordinal) ? "https:" + piece : piece;
                    if (url.Length <= 2000 && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo)
                        && !images.Contains(uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase) && images.Count < MaxImages)
                        images.Add(uri.AbsoluteUri);
                }

        var brand = First(item, mapping, FeedRoles.Brand);
        if (brand is not null) { brand = WebUtility.HtmlDecode(brand).Trim(); if (brand.Length > 200) brand = brand[..200]; }
        var desc = First(item, mapping, FeedRoles.Description);
        if (desc is not null) { desc = PlainText(desc); if (desc.Length > 5000) desc = desc[..5000]; if (desc.Length == 0) desc = null; }
        var mainId = First(item, mapping, FeedRoles.MainId);
        if (mainId is { Length: > 100 }) mainId = mainId[..100];

        if (problems.Count > 0) return (null, problems);
        return (new FeedRow(barcode!, name!, decimal.Round(price, 2, MidpointRounding.AwayFromZero), stock, inStock, brand, NormalizeCategory(First(item, mapping, FeedRoles.Category)), images, desc, vat, mainId), problems);
    }
}

/// <summary>Çözümleme ve önizleme için gereken besleme ayarları (şimdilik yalnızca stok sözlüğü; ileride genişler).</summary>
public record FeedOptions(bool PriceIncludesVat = true, int DefaultVatRate = 20, int AvailableStock = 10);
