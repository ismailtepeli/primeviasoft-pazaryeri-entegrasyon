# B2B kullanıcı rehberi

Bu rehber, yönetim panelini B2B için kullanacak kişilere yöneliktir. Bayiler için ayrı bir site (storefront) vardır; panel yalnızca sizin içindir.

## 1. Modülleri açma (Ayarlar > Modüller)

Her özellik bir modüldür; kapalı modülün menüsü görünmez, API'si 403 döner ve arka plan işleri çalışmaz. **Veri silinmez**; modülü yeniden açınca her şey geri gelir.

| Modül | Ne sağlar | Önkoşul |
|---|---|---|
| Bayi yönetimi (`dealers`) | Bayiler, bayi grupları, başvuru onayı, cari hesap | Çekirdek |
| Fiyat listeleri (`price-lists`) | Bayiye/gruba özel fiyat, iskonto, Excel ile içe aktarma | Bayi yönetimi |
| B2B bayi sitesi (`b2b-store`) | Bayi sitesi, B2B görünürlük, B2B siparişleri | Bayi yönetimi |
| Siparişler (`orders`) | B2B siparişleri de Siparişler ekranında "B2B" kanalıyla görünür | Çekirdek |

Yalnızca B2B kullanacaksanız `marketplace`, `stock-price` ve `invoices` modüllerini kapatabilirsiniz.

## 2. Bayi sitesi (storefront)

- Her firmanın kendi adresi vardır: `firma.<alan-adı>` (geliştirmede `http://localhost:5174/t/<firma-kodu>`).
- Bayi, sitede **başvuru** yapar. Başvuru Bayiler ekranında "Onay bekliyor" olarak görünür.
- Siz onaylayınca bayi giriş yapabilir. Onay sırasında **bayi grubunu** atayın; fiyat ve görünürlük kuralları gruba göre çalışır.
- Modül kapalıysa site "hizmet dışı" sayfası gösterir.

## 3. Fiyat listeleri

Bayiye gösterilen fiyat şu sırayla belirlenir:

1. Bayiye atanmış fiyat listesi
2. Bayinin grubuna atanmış fiyat listesi
3. Ürünün genel fiyatı (KDV dahil)

Liste kalemi **net fiyat** ya da genel fiyat üzerinden **iskonto %** olabilir. Listede fiyatın KDV dahil mi hariç mi olduğunu işaretlersiniz; sipariş toplamları buna göre net ve brüt olarak hesaplanır. Bayi sitesinde fiyatın KDV dahil/hariç gösterimi B2B ayarlarından seçilir.

Ürün ekranında **minimum sipariş** ve **koli (paket) adedi** tanımlanır; sepet bunlara uymayan miktarı 400 ile reddeder, bayi sitesindeki adet seçici koli adedi kadar artar.

## 4. Ürün görünürlüğü ve kategoriler

- Ürünün **B2B'de görünür** işareti kapalıysa bayi sitesinde hiç çıkmaz.
- Kategori ağacında bir kategoriyi **B2B'den gizlemek** altındaki ürünleri de gizler.
- Bayi grubuna özel görünürlük kuralı ile belirli kategori/ürünler yalnızca belirli gruplara açılabilir.
- Stok gösterimi: tam adet, seviye (var/az/yok) ya da gizli.

## 5. B2B siparişleri

Sipariş akışı: **Onay bekliyor → Onayla → Hazırlanıyor → Sevk et → Sevk edildi → Teslim edildi.**

- **Reddet** (gerekçe zorunlu) ve **İptal** siparişi iptal eder ve stoğu havuza geri ekler. Sevk edilmiş sipariş iptal edilemez.
- **Sevk et** için kargo firması ve takip numarası zorunludur; bayi bunları sitede görür.
- Sipariş verilince stok ortak havuzdan düşer. Pazaryeri ve B2B aynı stoğu kullanır; toplam stok aşılamaz.
- Genel Bakış'ta "B2B onay bekleyen" kartı bekleyen sipariş sayısını gösterir.

## 6. Cari hesap

Bayi düzenleme panelindeki Cari hesap bölümünde: toplam sipariş, ödemeler, bakiye (pozitif = bayi borçlu), vadesi geçen tutar. Ödemeleri elle girersiniz; en eski siparişten başlayarak kapatılır. Vade günü (0–365) bayi bazında verilir.

## Bilinen sınırlar

Online ödeme, kredi limiti, e-posta bildirimleri ve sevk sonrası iade henüz yok (bkz. açık görevler #73–#79).
