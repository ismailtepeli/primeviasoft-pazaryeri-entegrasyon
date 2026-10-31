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

### Parola işlemleri ve e-postalar

- Bayi **Parolamı unuttum** ile e-postasına tek kullanımlık, 60 dakika geçerli bir sıfırlama bağlantısı alır. E-posta kayıtlı olmasa da ekranda aynı mesaj çıkar (hesap bilgisi sızmaz); aynı adrese bir dakikada bir bağlantı gider.
- Giriş yapmış bayi **Hesabım** sayfasından mevcut parolasını doğrulayarak parolasını değiştirir. Parola değişince açık sıfırlama bağlantıları geçersiz olur.
- Başvuru **onaylanınca** ya da **reddedilince** firma yetkilisine e-posta gider; e-posta gönderilemese bile onay/ret işlemi kaydedilir.
- Gerçek e-posta sağlayıcısı bağlanana kadar e-postalar yalnızca sunucu günlüğüne yazılır, gönderilmez.

## 3. Fiyat listeleri

Bayiye gösterilen fiyat şu sırayla belirlenir:

1. Bayiye atanmış fiyat listesi
2. Bayinin grubuna atanmış fiyat listesi
3. Ürünün genel fiyatı (KDV dahil)

Liste kalemi **net fiyat** ya da genel fiyat üzerinden **iskonto %** olabilir. Listede fiyatın KDV dahil mi hariç mi olduğunu işaretlersiniz; sipariş toplamları buna göre net ve brüt olarak hesaplanır. Fiyat listesi dışındaki (genel) fiyatın bayi sitesinde KDV dahil mi hariç mi gösterileceğini **Ayarlar > B2B > Fiyat gösterimi** bölümünden seçersiniz (varsayılan: KDV dahil). Bu yalnızca gösterimi değiştirir; sipariş toplamları (KDV hariç, KDV, genel toplam) aynı kalır. Fiyat listeleri kendi gösterim ayarını kullanır.

**Excel ile toplu fiyat:** Fiyat listesi düzenleme ekranında **Şablonu indir** boş bir Excel verir (başlıklar: Barkod, Fiyat, Minimum, Koli). Doldurup **Excel ile yükle** ile yüklersiniz; hatalı satırlar satır numarasıyla gösterilir. **Fiyatları Excel'e aktar** listedeki fiyatlı ürünleri aynı biçimde indirir; dosyayı düzenleyip aynı listeye ya da başka bir listeye geri yükleyebilirsiniz. Minimum ve Koli sütunları bu listedeki kalemi etkiler (aşağıya bakın); boş bırakırsanız ürünün değeri geçerli olur. Barkodlar metin olarak saklanır, baştaki sıfırlar bozulmaz.

**Minimum sipariş ve koli adedi** iki düzeyde tanımlanır; sepet bunlara uymayan miktarı reddeder, bayi sitesindeki adet seçici koli sayar (1, 2, 3 koli):

1. **Ürün varsayılanı:** Fiyat listesi düzenleme ekranındaki "Ürün varsayılanı · Min · Koli" sütunu. Listede geçersiz kılma yoksa her yerde geçerlidir.
2. **Bu listede (kalem düzeyi):** "Bu listede: fiyat · Min · Koli" sütunundaki Min ve Koli alanları yalnızca o listedeki kalemi geçersiz kılar. Boş bırakılan alan ürünün değerini kullanır (yer tutucuda görünür). Geçersiz kılma için kalemin liste fiyatı olmalıdır; fiyat silinirse kalem ve adet ayarı da gider.

Bayiye uygulanan değer alan alan şu sırayla seçilir: **bayiye özel liste kalemi > bayi grubu listesi kalemi > ürün varsayılanı**. Örneğin bayi listesinde yalnızca koli 5 yazılıysa minimum grup listesinden ya da üründen gelir; sonuç her zaman koli katına yuvarlanır.

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

### Hızlı sipariş (bayi sitesi)

Bayi, üst çubuktaki **Hızlı sipariş** sayfasında ürün kodu (barkod) ve adet listesini yapıştırarak ya da Excel/CSV yükleyerek sepeti doldurur.

- Satır biçimi: `KOD;adet`, sekmeyle ayrılmış (Excel'den kopyalama), `KOD,adet`, `KOD 12` ya da `KOD x 12`. Adet yazılmazsa 1 sayılır; ilk satırdaki "Kod/Barkod" başlığı atlanır.
- Dosyada ilk sütun kod, ikinci sütun adettir (.xlsx, .csv; en fazla 2 MB ve 1000 satır).
- Geçerli satırlar sepete eklenir, sorunlu satırlar **satır numarasıyla** listelenir (bilinmeyen kod, koli/minimum uyumsuzluğu, stok yetersiz, geçersiz adet). Aynı kod birden çok satırda geçerse adetler toplanır; sepette zaten varsa üstüne eklenir.
- Bayiye görünmeyen ürünün kodu "Ürün bulunamadı" döner; ürünün var olduğu sızmaz.

## 6. Cari hesap

Bayi düzenleme panelindeki Cari hesap bölümünde: toplam sipariş, ödemeler, bakiye (pozitif = bayi borçlu), vadesi geçen tutar. Ödemeleri elle girersiniz; en eski siparişten başlayarak kapatılır. Vade günü (0–365) bayi bazında verilir.

## Bilinen sınırlar

Online ödeme, kredi limiti, sipariş durumu e-postaları ve sevk sonrası iade henüz yok; e-postalar gerçek sağlayıcı bağlanana kadar yalnızca günlüğe yazılır (bkz. açık görevler #73–#79).
