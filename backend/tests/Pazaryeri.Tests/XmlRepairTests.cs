using System.Text;
using Pazaryeri.Infrastructure.Feeds;

namespace Pazaryeri.Tests;

/// <summary>Bozuk ama yaygın XML'lerin (kaçışsız &amp;, başta artık metin) reddedilmeden okunması ve Google Merchant eşlemesi.</summary>
public class XmlRepairTests
{
    private static MemoryStream Bytes(string text, Encoding? enc = null) => new((enc ?? new UTF8Encoding(false)).GetBytes(text));
    private static FeedOptions Opt => new();

    // Google'ın örnek besleme dosyasının küçültülmüşü: tarayıcıdan kopyalanmış ilk satır, kaçışsız &, CDATA, iç içe shipping/price,
    // yalnızca bir üründe sale_price, bazı ürünlerde gtin yok.
    private const string Merchant = """
        This XML file does not appear to have any style information associated with it. The document tree is shown below.
        <rss xmlns:g="http://base.google.com/ns/1.0" version="2.0">
        <channel>
        <title>Example - Online Store</title>
        <link>http://www.example.com</link>
        <!-- yorum -- iki tire içeriyor -->
        <item>
        <g:id>TV_123456</g:id>
        <g:title>LG 22" LED TV</g:title>
        <g:description>Güzel bir televizyon</g:description>
        <g:link>http://www.example.com/tv.html</g:link>
        <g:image_link>http://images.example.com/TV_123456.png</g:image_link>
        <g:availability>in stock</g:availability>
        <g:price>159.00 USD</g:price>
        <g:shipping><g:country>US</g:country><g:price>14.95 USD</g:price></g:shipping>
        <g:gtin>71919219405200</g:gtin>
        <g:brand>LG</g:brand>
        <g:product_type>Electronics > TVs</g:product_type>
        </item>
        <item>
        <g:id>PFM654321</g:id>
        <g:title><![CDATA[ Dior Capture & Co ]]></g:title>
        <g:link><![CDATA[ http://www.example.com/p?sku=1&src=gshopping&lang=en ]]></g:link>
        <g:image_link>http://images.example.com/PFM_1.jpg?size=large&format=PNG</g:image_link>
        <g:additional_image_link>http://images.example.com/PFM_2.jpg</g:additional_image_link>
        <g:availability>in stock</g:availability>
        <g:price>99 USD</g:price>
        <g:shipping><g:country>US</g:country><g:price>4.95 USD</g:price></g:shipping>
        <g:shipping><g:country>CA</g:country><g:price>8.50 USD</g:price></g:shipping>
        <g:gtin>3348901056069</g:gtin>
        <g:brand>M&S</g:brand>
        <g:product_type>Health & Beauty > Personal Care</g:product_type>
        </item>
        <item>
        <g:id>CLO-29473856-1</g:id>
        <g:title>Jeans&nbsp;Mavi</g:title>
        <g:availability>out of stock</g:availability>
        <g:price>29.50 USD</g:price>
        <g:mpn>B003J5F5EY</g:mpn>
        <g:brand>M&S</g:brand>
        <g:sale_price>25.49 USD</g:sale_price>
        </item>
        </channel>
        </rss>
        """;

    [Fact]
    public void Basinda_artik_metin_ve_kacissiz_ampersand_olan_Google_Merchant_beslemesi_okunur()
    {
        var a = XmlFeed.Analyze(Bytes(Merchant), null, null, Opt);

        Assert.Equal("rss/channel/item", a.ItemPath);
        Assert.Equal("RSS / Google Merchant", a.Format);
        Assert.Equal(3, a.ItemCount);
        Assert.Empty(a.Missing);
    }

    [Fact]
    public void Google_beslemesinde_barkod_id_fiyat_price_olur_kismi_dolu_gtin_sale_price_ve_shipping_price_secilmez()
    {
        var a = XmlFeed.Analyze(Bytes(Merchant), null, null, Opt);

        Assert.Equal(["id"], a.Mapping["barcode"]);      // gtin 3 üründen 2'sinde var
        Assert.Equal(["title"], a.Mapping["name"]);
        Assert.Equal(["price"], a.Mapping["price"]);     // sale_price tek üründe, shipping/price iç içe
        Assert.Equal(["availability"], a.Mapping["stock"]);
        Assert.Equal(3, a.Preview.Count);
        Assert.All(a.Preview, p => Assert.Empty(p.Problems));
        Assert.Equal(("TV_123456", 159.00m), (a.Preview[0].Barcode, a.Preview[0].Price));
    }

    [Fact]
    public void Kacissiz_karakterler_metin_olarak_korunur_CDATA_bozulmaz()
    {
        var a = XmlFeed.Analyze(Bytes(Merchant), null, null, Opt);

        Assert.Equal("Dior Capture & Co", a.Preview[1].Name);
        Assert.Equal("M&S", a.Preview[1].Brand);
        Assert.Equal("Jeans Mavi", a.Preview[2].Name); // &nbsp; düz metin kalır, ad dönüşümünde boşluğa çevrilir
        Assert.False(a.Preview[2].Stock > 0);
        var items = XmlFeed.ReadItems(Bytes(Merchant), a.ItemPath).ToList();
        Assert.Equal("http://www.example.com/p?sku=1&src=gshopping&lang=en", items[1].Values["link"][0]);
        Assert.Equal("Health & Beauty > Personal Care", items[1].Values["product_type"][0]);
    }

    [Fact]
    public void Gecerli_varlik_basvurulari_bozulmaz()
    {
        const string xml = "<Urunler><Urun><Barkod>1</Barkod><UrunAdi>A &amp; B &lt;x&gt; &#8364; &#x20AC; &quot;q&quot;</UrunAdi><Fiyat>5</Fiyat></Urun>"
            + "<Urun><Barkod>2</Barkod><UrunAdi>x &#xyz; y &</UrunAdi><Fiyat>6</Fiyat></Urun></Urunler>";

        var items = XmlFeed.ReadItems(Bytes(xml), "Urunler/Urun").ToList();

        Assert.Equal("A & B <x> € € \"q\"", items[0].Values["UrunAdi"][0]);
        Assert.Equal("x &#xyz; y &", items[1].Values["UrunAdi"][0]);
    }

    [Fact]
    public void Kacissiz_kucuktur_isareti_metin_olur()
    {
        const string xml = "<Urunler><Urun><Barkod>1</Barkod><UrunAdi>Fiyat < 5 TL</UrunAdi><Fiyat>5</Fiyat></Urun></Urunler>";
        Assert.Equal("Fiyat < 5 TL", XmlFeed.ReadItems(Bytes(xml), "Urunler/Urun").Single().Values["UrunAdi"][0]);
    }

    [Fact]
    public void Buyuk_dosyada_onarim_parca_sinirlarinda_dogru_calisir()
    {
        // 16 KB'lık ara belleği ve 4096 karakterlik parçaları aşan, sınırlara denk gelen & ve CDATA içeren çok satırlı dosya.
        var sb = new StringBuilder("junk\n<Urunler>");
        for (var i = 0; i < 3000; i++)
            sb.Append($"<Urun><Barkod>B{i}</Barkod><UrunAdi>Ürün {i} & Ortağı</UrunAdi><Fiyat>{i + 1},50</Fiyat><Not><![CDATA[a & <b> ]]]]></Not></Urun>");
        sb.Append("</Urunler>");

        var items = XmlFeed.ReadItems(Bytes(sb.ToString()), "Urunler/Urun").ToList();

        Assert.Equal(3000, items.Count);
        Assert.Equal("Ürün 2999 & Ortağı", items[^1].Values["UrunAdi"][0]);
        Assert.Equal("a & <b> ]]", items[1500].Values["Not"][0].Trim());
    }

    [Fact]
    public void Kodlama_bildirimi_ve_BOMsuz_UTF16_okunur()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var xml = "<?xml version=\"1.0\" encoding=\"windows-1254\"?><Urunler><Urun><Barkod>1</Barkod><UrunAdi>Şeker & Çay</UrunAdi><Fiyat>5</Fiyat></Urun>"
            + "<Urun><Barkod>2</Barkod><UrunAdi>B</UrunAdi><Fiyat>6</Fiyat></Urun></Urunler>";
        var tr = XmlFeed.ReadItems(Bytes(xml, Encoding.GetEncoding(1254)), "Urunler/Urun").First();
        Assert.Equal("Şeker & Çay", tr.Values["UrunAdi"][0]);

        var utf16 = xml.Replace("windows-1254", "utf-16");
        var item = XmlFeed.ReadItems(Bytes(utf16, new UnicodeEncoding(false, false)), "Urunler/Urun").First();
        Assert.Equal("Şeker & Çay", item.Values["UrunAdi"][0]);
    }

    [Fact]
    public void Hala_bozuk_ya_da_xml_olmayan_icerik_reddedilir()
    {
        Assert.Throws<Application.Feeds.FeedException>(() => XmlFeed.Analyze(Bytes("<a><b></a>"), null, null, Opt));
        Assert.Throws<Application.Feeds.FeedException>(() => XmlFeed.Analyze(Bytes("Sadece düz metin, etiket yok"), null, null, Opt));
        Assert.Throws<Application.Feeds.FeedException>(() => XmlFeed.Analyze(Bytes(""), null, null, Opt));
    }
}
