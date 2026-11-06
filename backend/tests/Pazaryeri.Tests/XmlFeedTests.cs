using System.Net;
using System.Text;
using Pazaryeri.Application.Feeds;
using Pazaryeri.Infrastructure.Feeds;

namespace Pazaryeri.Tests;

/// <summary>Tedarikçi XML'inin (biçimi önceden bilinmeden) tanınması, alan eşlemesi ve değer dönüştürme.</summary>
public class XmlFeedTests
{
    private static MemoryStream Xml(string text, Encoding? encoding = null) => new((encoding ?? new UTF8Encoding(false)).GetBytes(text));

    private static FeedAnalysis Analyze(string xml) => XmlFeed.Analyze(Xml(xml), null, null, new FeedOptions());

    private const string Ticimax = """
        <?xml version="1.0" encoding="utf-8"?>
        <Urunler>
          <Urun>
            <UrunKartiID>501</UrunKartiID>
            <StokKodu>TS-100</StokKodu>
            <Barkod>8690000000011</Barkod>
            <UrunAdi>Basic Tişört Mavi</UrunAdi>
            <Marka>Örnek Marka</Marka>
            <Kategori>Giyim / Tişört</Kategori>
            <SatisFiyati>1.234,50</SatisFiyati>
            <KdvOrani>20</KdvOrani>
            <StokAdedi>25</StokAdedi>
            <Resim1>https://cdn.example.com/a1.jpg</Resim1>
            <Resim2>https://cdn.example.com/a2.jpg</Resim2>
            <Aciklama><![CDATA[<p>Yumuşak <b>pamuk</b></p><p>Yıkanabilir</p>]]></Aciklama>
          </Urun>
          <Urun>
            <UrunKartiID>502</UrunKartiID>
            <StokKodu>TS-101</StokKodu>
            <Barkod>8690000000028</Barkod>
            <UrunAdi>Basic Tişört Siyah</UrunAdi>
            <Marka>Örnek Marka</Marka>
            <Kategori>Giyim / Tişört</Kategori>
            <SatisFiyati>999,90</SatisFiyati>
            <KdvOrani>20</KdvOrani>
            <StokAdedi>0</StokAdedi>
            <Resim1>https://cdn.example.com/b1.jpg</Resim1>
          </Urun>
        </Urunler>
        """;

    private const string Google = """
        <?xml version="1.0"?>
        <rss version="2.0" xmlns:g="http://base.google.com/ns/1.0">
          <channel>
            <title>Mağaza</title><link>https://magaza.example.com</link>
            <item>
              <g:id>SKU-1</g:id>
              <title>Kablosuz Kulaklık</title>
              <description>Bluetooth 5.3, 30 saat pil</description>
              <link>https://magaza.example.com/u/1</link>
              <g:image_link>https://cdn.example.com/k1.jpg</g:image_link>
              <g:additional_image_link>https://cdn.example.com/k2.jpg</g:additional_image_link>
              <g:additional_image_link>https://cdn.example.com/k3.jpg</g:additional_image_link>
              <g:availability>in stock</g:availability>
              <g:price>799.90 TRY</g:price>
              <g:brand>SesTek</g:brand>
              <g:gtin>8690000000099</g:gtin>
            </item>
            <item>
              <g:id>SKU-2</g:id>
              <title>Kulaklık Kılıfı</title>
              <description>Sert kılıf</description>
              <link>https://magaza.example.com/u/2</link>
              <g:image_link>https://cdn.example.com/k4.jpg</g:image_link>
              <g:availability>out of stock</g:availability>
              <g:price>59.00 TRY</g:price>
              <g:brand>SesTek</g:brand>
              <g:gtin>8690000000105</g:gtin>
            </item>
          </channel>
        </rss>
        """;

    private const string English = """
        <catalog>
          <meta><generated>2026-11-01</generated><owner>Tedarikçi</owner><count>2</count><version>3</version></meta>
          <products>
            <product>
              <sku>EN-1</sku><name>Desk Lamp</name><price>24.5</price><quantity>12</quantity><manufacturer>Lumo</manufacturer>
              <category>Home &gt; Lighting</category>
              <images><image>https://cdn.example.com/l1.jpg</image><image>https://cdn.example.com/l2.jpg</image></images>
            </product>
            <product>
              <sku>EN-2</sku><name>Floor Lamp</name><price>89</price><quantity>3</quantity><manufacturer>Lumo</manufacturer>
              <category>Home &gt; Lighting</category>
              <images><image>https://cdn.example.com/l3.jpg</image></images>
            </product>
          </products>
        </catalog>
        """;

    [Fact]
    public void Turkce_alan_adlari_tanir_urun_ogesini_ve_eslemeyi_bulur()
    {
        var a = Analyze(Ticimax);

        Assert.Equal("Urunler/Urun", a.ItemPath);
        Assert.Equal(2, a.ItemCount);
        Assert.Empty(a.Missing);
        Assert.Equal("Barkod", a.Mapping[FeedRoles.Barcode][0]);
        Assert.Equal("UrunAdi", a.Mapping[FeedRoles.Name][0]);
        Assert.Equal("SatisFiyati", a.Mapping[FeedRoles.Price][0]);
        Assert.Equal("StokAdedi", a.Mapping[FeedRoles.Stock][0]);
        Assert.Equal("Marka", a.Mapping[FeedRoles.Brand][0]);
        Assert.Equal("Kategori", a.Mapping[FeedRoles.Category][0]);
        Assert.Equal("KdvOrani", a.Mapping[FeedRoles.Vat][0]);
        Assert.Equal("UrunKartiID", a.Mapping[FeedRoles.MainId][0]);
        Assert.Equal(["Resim1", "Resim2"], a.Mapping[FeedRoles.Images]);
    }

    [Fact]
    public void Turkce_besleme_onizlemede_degerleri_normallestirir()
    {
        var a = Analyze(Ticimax);

        var first = a.Preview[0];
        Assert.Equal(("8690000000011", "Basic Tişört Mavi", 1234.50m, 25), (first.Barcode, first.Name, first.Price, first.Stock));
        Assert.Equal("Giyim > Tişört", first.Category);
        Assert.Equal(2, first.ImageCount);
        Assert.Empty(first.Problems);
    }

    [Fact]
    public void Google_Merchant_RSS_g_onekli_alanlari_ve_stok_metnini_tanir()
    {
        var a = Analyze(Google);

        Assert.Equal("rss/channel/item", a.ItemPath);
        Assert.Equal("RSS / Google Merchant", a.Format);
        Assert.Equal("gtin", a.Mapping[FeedRoles.Barcode][0]);
        Assert.Equal("title", a.Mapping[FeedRoles.Name][0]);
        Assert.Equal("price", a.Mapping[FeedRoles.Price][0]);
        Assert.Equal("availability", a.Mapping[FeedRoles.Stock][0]);
        Assert.Equal(["additional_image_link", "image_link"], a.Mapping[FeedRoles.Images].Order());

        var map = a.Mapping.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        var rows = XmlFeed.ReadItems(Xml(Google), a.ItemPath).Select(i => XmlFeed.Map(i, map, new FeedOptions()).Row!).ToList();
        Assert.Equal((799.90m, true, null), (rows[0].Price, rows[0].InStock, rows[0].Stock));
        Assert.Equal(false, rows[1].InStock);
        Assert.Equal(3, rows[0].Images.Count); // image_link + 2 ek görsel
    }

    [Fact]
    public void Ingilizce_besleme_tekrarlanan_gorselleri_tek_yolla_okur_ve_ust_bilgi_ogesini_secmez()
    {
        var a = Analyze(English);

        Assert.Equal("catalog/products/product", a.ItemPath);
        Assert.Equal("sku", a.Mapping[FeedRoles.Barcode][0]);
        Assert.Equal("manufacturer", a.Mapping[FeedRoles.Brand][0]);
        Assert.Equal(["images/image"], a.Mapping[FeedRoles.Images]);
        Assert.Equal(("EN-1", 24.5m, 12), (a.Preview[0].Barcode, a.Preview[0].Price, a.Preview[0].Stock));
        Assert.Equal(2, a.Preview[0].ImageCount);
        Assert.Contains(a.Fields, f => f.Path == "images/image" && f.Repeated);
    }

    [Fact]
    public void Ozellikli_oge_alanlari_da_okunur()
    {
        const string xml = """
            <Products>
              <Product Barcode="111" Name="Kalem" Price="12,5" Stock="40" Brand="Faber" />
              <Product Barcode="222" Name="Silgi" Price="4" Stock="9" Brand="Faber" />
            </Products>
            """;

        var a = Analyze(xml);

        Assert.Equal("Products/Product", a.ItemPath);
        Assert.Equal(("111", "Kalem", 12.5m, 40), (a.Preview[0].Barcode, a.Preview[0].Name, a.Preview[0].Price, a.Preview[0].Stock));
    }

    [Fact]
    public void Windows_1254_kodlu_Turkce_karakterler_dogru_okunur()
    {
        var xml = Ticimax.Replace("utf-8", "windows-1254");
        var a = XmlFeed.Analyze(Xml(xml, Encoding.GetEncoding(1254)), null, null, new FeedOptions());
        Assert.Equal("Basic Tişört Mavi", a.Preview[0].Name);
    }

    [Fact]
    public void Basinda_bosluk_ya_da_BOM_olan_dosya_okunur()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("\r\n  " + Ticimax)).ToArray();
        Assert.Equal(2, XmlFeed.Analyze(new MemoryStream(bytes), null, null, new FeedOptions()).ItemCount);
    }

    [Fact]
    public void Web_sayfasi_bozuk_ve_bos_icerik_anlasilir_hatayla_reddedilir()
    {
        Assert.Contains("web sayfası", Assert.Throws<FeedException>(() => Analyze("<html><head><title>x</title></head><body><p>Giriş yapın</p></body></html>")).Message);
        Assert.Throws<FeedException>(() => Analyze("{\"products\": []}"));
        Assert.Throws<FeedException>(() => Analyze("<a><b></a>"));
        Assert.Throws<FeedException>(() => Analyze(""));
        Assert.Contains("ürün listesi bulunamadı", Assert.Throws<FeedException>(() => Analyze("<root><a>1</a><b>2</b></root>")).Message);
    }

    [Fact]
    public void DTD_bildirimi_yok_sayilir_dis_varlik_basvurusu_reddedilir_dosya_icerigi_okunmaz_XXE()
    {
        const string withDoctype = """
            <?xml version="1.0"?>
            <!DOCTYPE Urunler [ <!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini"> ]>
            <Urunler>
              <Urun><Barkod>1</Barkod><UrunAdi>A</UrunAdi><Fiyat>5</Fiyat><Stok>1</Stok></Urun>
              <Urun><Barkod>2</Barkod><UrunAdi>B</UrunAdi><Fiyat>6</Fiyat><Stok>2</Stok></Urun>
            </Urunler>
            """;
        Assert.Equal(2, XmlFeed.Analyze(Xml(withDoctype), null, null, new FeedOptions()).ItemCount); // DOCTYPE tek başına sorun değil

        var useEntity = withDoctype.Replace("<UrunAdi>A</UrunAdi>", "<UrunAdi>A &xxe;</UrunAdi>");
        Assert.Throws<FeedException>(() => XmlFeed.Analyze(Xml(useEntity), null, null, new FeedOptions())); // varlık çözülmez, güvenle reddedilir
    }

    [Fact]
    public void Milyar_gulen_varlik_genisletmesi_bellegi_sisirmez()
    {
        // Her varlık bir öncekinin 10 katını içerir; DTD yok sayıldığı için hiçbiri genişletilmez, başvuru güvenle reddedilir.
        var entities = string.Concat(Enumerable.Range(1, 9).Select(i => "<!ENTITY l" + i + " \"" + string.Concat(Enumerable.Repeat("&l" + (i - 1) + ";", 10)) + "\">"));
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE z [<!ENTITY l0 \"lol\">" + entities + "]><Urunler>"
            + "<Urun><Barkod>1</Barkod><UrunAdi>&l9;</UrunAdi><Fiyat>5</Fiyat></Urun><Urun><Barkod>2</Barkod><UrunAdi>B</UrunAdi><Fiyat>6</Fiyat></Urun></Urunler>";
        Assert.Throws<FeedException>(() => XmlFeed.Analyze(Xml(xml), null, null, new FeedOptions()));
    }

    [Fact]
    public void Zorunlu_alan_eslenemezse_eksik_olarak_bildirilir_onizleme_bos_kalir()
    {
        const string xml = """
            <Liste>
              <Satir><Kod>1</Kod><Aciklama>a</Aciklama><Not>x</Not></Satir>
              <Satir><Kod>2</Kod><Aciklama>b</Aciklama><Not>y</Not></Satir>
            </Liste>
            """;

        var a = Analyze(xml);

        Assert.Contains(FeedRoles.Name, a.Missing);
        Assert.Contains(FeedRoles.Price, a.Missing);
        Assert.Empty(a.Preview);
    }

    [Fact]
    public void Kullanicinin_elle_sectigi_esleme_ve_oge_yolu_otomatik_tanimanin_yerine_gecer()
    {
        const string xml = """
            <Urunler>
              <Urun><Kod>1</Kod><Isim>Ürün A</Isim><Alis>10</Alis><Satis>15</Satis><Adet>3</Adet></Urun>
              <Urun><Kod>2</Kod><Isim>Ürün B</Isim><Alis>20</Alis><Satis>30</Satis><Adet>4</Adet></Urun>
            </Urunler>
            """;
        var mapping = new Dictionary<string, List<string>>
        {
            [FeedRoles.Barcode] = ["Kod"], [FeedRoles.Name] = ["Isim"], [FeedRoles.Price] = ["Satis"], [FeedRoles.Stock] = ["Adet"], ["bilinmeyen"] = ["Kod"], [FeedRoles.Brand] = ["Yok"],
        };

        var a = XmlFeed.Analyze(Xml(xml), "Urunler/Urun", mapping, new FeedOptions());

        Assert.Equal(15m, a.Preview[0].Price);
        Assert.False(a.Mapping.ContainsKey("bilinmeyen"));
        Assert.False(a.Mapping.ContainsKey(FeedRoles.Brand)); // öğede olmayan yol atılır
        Assert.Throws<FeedException>(() => XmlFeed.Analyze(Xml(xml), "Urunler/Yok", null, new FeedOptions()));
    }

    [Fact]
    public void Besleme_urun_basina_birden_cok_varyant_iceriyorsa_varyant_satirini_secer()
    {
        const string xml = """
            <Urunler>
              <Urun>
                <Ad>Kazak</Ad>
                <Varyasyonlar>
                  <Varyasyon><Barkod>V1</Barkod><Fiyat>100</Fiyat><Stok>2</Stok><Beden>S</Beden></Varyasyon>
                  <Varyasyon><Barkod>V2</Barkod><Fiyat>100</Fiyat><Stok>5</Stok><Beden>M</Beden></Varyasyon>
                </Varyasyonlar>
              </Urun>
              <Urun>
                <Ad>Hırka</Ad>
                <Varyasyonlar>
                  <Varyasyon><Barkod>V3</Barkod><Fiyat>150</Fiyat><Stok>1</Stok><Beden>M</Beden></Varyasyon>
                </Varyasyonlar>
              </Urun>
            </Urunler>
            """;

        var a = Analyze(xml);

        // Üst öğede barkod ürün başına birden çok kez geliyor; satır olarak varyant öğesi seçilir.
        Assert.Equal("Urunler/Urun/Varyasyonlar/Varyasyon", a.ItemPath);
        Assert.Equal(3, a.ItemCount);
        Assert.Contains(a.Candidates, c => c.Path == "Urunler/Urun");
    }

    // --- değer dönüştürme ---

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("12,5", 12.5)]
    [InlineData("12,500", 12500)]
    [InlineData("799.90 TRY", 799.90)]
    [InlineData("₺99", 99)]
    [InlineData("1.250.000", 1250000)]
    [InlineData("0", 0)]
    public void Sayilar_Turkce_ve_Ingilizce_yazimla_okunur(string text, double expected)
    {
        Assert.True(XmlFeed.TryParseNumber(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-")]
    public void Sayi_olmayan_metin_reddedilir(string text) => Assert.False(XmlFeed.TryParseNumber(text, out _));

    [Theory]
    [InlineData("in stock", true)]
    [InlineData("Stokta Var", true)]
    [InlineData("out of stock", false)]
    [InlineData("Stokta Yok", false)]
    [InlineData("belki", null)]
    public void Stok_metinleri_anlasilir(string text, bool? expected) => Assert.Equal(expected, XmlFeed.StockWord(text));

    [Theory]
    [InlineData("Giyim / Tişört", "Giyim > Tişört")]
    [InlineData("Giyim | Tişört | Basic", "Giyim > Tişört > Basic")]
    [InlineData("Giyim > Tişört", "Giyim > Tişört")]
    [InlineData("  ", null)]
    [InlineData("A > B > C > D > E > F > G", "A > B > C > D > E")]
    public void Kategori_yollari_tek_bicime_getirilir(string raw, string? expected) => Assert.Equal(expected, XmlFeed.NormalizeCategory(raw));

    [Fact]
    public void Html_aciklama_duz_metne_cevrilir_script_atilir()
    {
        var text = XmlFeed.PlainText("<p>Yumuşak <b>pamuk</b></p><script>alert(1)</script><p>Yıkanabilir &amp; dayanıklı</p><br>Son");
        Assert.DoesNotContain("<", text);
        Assert.DoesNotContain("alert", text);
        Assert.Contains("Yumuşak pamuk", text);
        Assert.Contains("Yıkanabilir & dayanıklı", text);
    }

    [Fact]
    public void Zorunlu_alanlari_eksik_ya_da_gecersiz_satir_nedenleriyle_reddedilir()
    {
        var item = new FeedItem();
        item.Add("Ad", "Ürün"); item.Add("Fiyat", "0");
        var map = new Dictionary<string, List<string>> { [FeedRoles.Barcode] = ["Kod"], [FeedRoles.Name] = ["Ad"], [FeedRoles.Price] = ["Fiyat"] };

        var (row, problems) = XmlFeed.Map(item, map, new FeedOptions());

        Assert.Null(row);
        Assert.Equal(["Barkod/ürün kodu boş.", "Fiyat geçersiz."], problems);
    }

    [Theory]
    [InlineData("18", 18)]
    [InlineData("0.18", 18)]
    [InlineData("%20", 20)]
    [InlineData("0", 0)]
    [InlineData("250", null)]
    public void KDV_orani_yuzde_ya_da_kesir_olarak_okunur(string text, int? expected)
    {
        var item = new FeedItem();
        item.Add("K", "1"); item.Add("A", "x"); item.Add("F", "5"); item.Add("V", text);
        var map = new Dictionary<string, List<string>> { [FeedRoles.Barcode] = ["K"], [FeedRoles.Name] = ["A"], [FeedRoles.Price] = ["F"], [FeedRoles.Vat] = ["V"] };

        Assert.Equal(expected, XmlFeed.Map(item, map, new FeedOptions()).Row!.Vat);
    }

    [Fact]
    public void Gorsel_adresleri_yalnizca_http_https_olur_tekrar_ve_ust_siniri_uygulanir()
    {
        var item = new FeedItem();
        item.Add("K", "1"); item.Add("A", "x"); item.Add("F", "5");
        item.Add("R", "https://a.example.com/1.jpg, //cdn.example.com/2.jpg; javascript:alert(1) | ftp://x/3.jpg https://a.example.com/1.jpg");
        for (var i = 0; i < 12; i++) item.Add("R", $"https://a.example.com/x{i}.jpg");
        var map = new Dictionary<string, List<string>> { [FeedRoles.Barcode] = ["K"], [FeedRoles.Name] = ["A"], [FeedRoles.Price] = ["F"], [FeedRoles.Images] = ["R"] };

        var images = XmlFeed.Map(item, map, new FeedOptions()).Row!.Images;

        Assert.Equal(8, images.Count);
        Assert.Equal("https://a.example.com/1.jpg", images[0]);
        Assert.Equal("https://cdn.example.com/2.jpg", images[1]);
        Assert.All(images, i => Assert.StartsWith("http", i));
    }

    // --- SSRF: adres güvenliği ---

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:10.0.0.5", false)]
    [InlineData("2606:4700:4700::1111", true)]
    public void Ozel_ve_yerel_adresler_engellenir_halka_acik_adresler_gecer(string ip, bool expected) => Assert.Equal(expected, FeedFetcher.IsPublic(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("")]
    [InlineData("ftp://x.example.com/f.xml")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@x.example.com/f.xml")]
    [InlineData("tedarikci.com/feed.xml")]
    public void Gecersiz_besleme_adresleri_reddedilir(string url) => Assert.Throws<FeedException>(() => FeedFetcher.ValidateUrl(url));

    [Fact]
    public void Sorgu_anahtarli_https_adres_kabul_edilir() => Assert.Equal("x.example.com", FeedFetcher.ValidateUrl("https://x.example.com/feed.xml?key=abc").Host);
}
