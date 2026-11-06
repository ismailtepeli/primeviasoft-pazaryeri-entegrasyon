# Kullanım kılavuzu

Pazaryeri Entegrasyon; pazaryeri (Trendyol, Hepsiburada, N11, Amazon), B2B bayi sitesi ve B2C mağaza satışlarını tek yönetim panelinden yönetmenizi sağlar. Bu kılavuz uygulamayı ilk kez kullananlar içindir. B2B'ye özel ayrıntılar için [B2B kullanıcı rehberine](b2b-kullanici-rehberi.md), kurulum ve ayarlar için [README](../README.md) dosyasına bakın.

## İçindekiler
1. [Uygulama üç parçadan oluşur](#1-uygulama-üç-parçadan-oluşur)
2. [İlk giriş ve kurulum sihirbazı](#2-ilk-giriş-ve-kurulum-sihirbazı)
3. [Genel bakış](#3-genel-bakış)
4. [Ürünler](#4-ürünler)
5. [Pazaryerleri](#5-pazaryerleri)
6. [Siparişler](#6-siparişler)
7. [B2B bayi sitesi](#7-b2b-bayi-sitesi)
8. [B2C mağaza](#8-b2c-mağaza)
9. [Ayarlar](#9-ayarlar)
10. [Sık sorulanlar](#10-sık-sorulanlar)

## 1. Uygulama üç parçadan oluşur

| Parça | Kimin için | Geliştirme adresi |
|---|---|---|
| Yönetim paneli | Siz (satıcı/yönetici) | `http://localhost:5173` |
| B2B bayi sitesi | Bayileriniz | `http://localhost:5174/t/<firma-kodu>` |
| B2C mağaza | Son tüketiciler | `http://localhost:5176/t/<firma-kodu>` |

Ürün, kategori ve stok bilgisi tek yerde, yönetim panelinde tutulur; satış kanalları bu ortak kataloğu kullanır. Pazaryeri ve B2B aynı stok havuzundan düşer, toplam stok aşılamaz.

Geliştirme sunucusunda üst çubuktaki rozet ve sekme başlığındaki `[B2C]` / `[B2B]` öneki hangi sitede olduğunuzu gösterir (üretimde görünmez).

## 2. İlk giriş ve kurulum sihirbazı

1. Yönetim paneline e-posta ve parolanızla girin. **Beni hatırla** işaretliyse oturum kalıcı olur; değilse tarayıcı kapanınca biter.
2. Yeni hesapta **"Nerede satış yapacaksınız?"** sihirbazı çıkar. Satış kanallarını (Pazaryerleri, B2B, B2C) seçip **Kurulumu tamamla**'ya basın. Yalnızca seçtiğiniz kanalların menüleri açılır.
3. Seçimi sonradan **Ayarlar > Satış kanalları**'ndan değiştirebilirsiniz. Kanal kapatmak veri silmez; menü gizlenir ve arka plan işleri durur.

Geliştirme ortamında hazır veri için `seed-demo` komutunu çalıştırın (bkz. README); `admin@demo.local` ile girip kataloğu hemen deneyebilirsiniz.

## 3. Genel bakış

Açılış ekranı mağaza, stok ve sipariş durumunuzu özetler: kanal durumları, bekleyen siparişler (ör. "B2B onay bekleyen") ve senkron hataları. Sağ üstteki **Şimdi senkronla** düğmesi kanallarla senkronu hemen başlatır. Sağ üstteki zil sipariş ve süreç bildirimlerini gösterir.

## 4. Ürünler

**Ürünler** sayfası tüm kataloğu ve her ürünün kanal durumunu listeler.

- **Yeni ürün:** **Ürün ekle** ile bilgileri (ad, barkod, kategori, fiyat, KDV, stok) girip kaydedin; görselleri kaydettikten sonra eklersiniz.
- **Excel ile yükleme:** Çok ürünü tek dosyayla eklemek için **Excel ile yükle**'yi kullanın. Barkodu kayıtlı olan ürünler güncellenir. Barkodlar metin olarak saklanır, baştaki sıfırlar bozulmaz.
- **Kategoriler:** Kategori ağacını yönetebilir, ürünleri kategoriye göre süzebilirsiniz.
- **Görseller:** Ürün düzenleme sayfasında görsel ekleyin, sırasını değiştirin. İlk görsel ana görseldir.
- **Fiyat güncelleme:** Satır içi (fiyata tıklayıp Enter), seçili ürünlere toplu (yüzde, tutar, sabit fiyat) veya Excel ile. Ayrıntı için B2B rehberinin 7. bölümü. Yeni fiyat kanallara bir sonraki stok-fiyat senkronunda gider.
- **Toplu işlem:** Ürünleri işaretleyince çıkan çubuktan fiyat değiştirme, B2C'de gösterme/kaldırma ve B2C kampanya uygulayabilirsiniz.
- **XML ile ürün aktarımı (dropshipping):** Sol menüdeki **XML aktarım** sayfasından tedarikçinin XML adresini ekleyin.
  1. **Besleme ekle** > ad ve adresi yazıp **Adresi çözümle**'ye basın. Sistem XML'in biçimini kendisi tanır (Ticimax/T-Soft tarzı Türkçe alan adları, Google Merchant/RSS, İngilizce alan adları ve öznitelikli XML desteklenir) ve barkod, ad, fiyat, stok, marka, kategori, görsel, açıklama, KDV alanlarını eşler.
  2. **Alan eşleme** tablosunda yanlış eşleneni değiştirin, **Önizleme**'de ilk ürünlerin doğru okunduğunu görün. Barkod, ad ve fiyat zorunludur. Yanlış satır seçildiyse (örn. varyant yerine ürün) **Ürün öğesi** listesinden değiştirin.
  3. **Kâr yüzdesi** girin: satış fiyatı = tedarikçi fiyatı × (1 + kâr %); tedarikçi fiyatı ürünün maliyetine yazılır. Tedarikçi fiyatı KDV hariçse kutuyu kapatın.
  4. Yeni ürünlerin B2C/B2B'de hemen görünmesini isteyip istemediğinizi seçin (kapalıysa yalnızca katalogda olur) ve **Kaydet ve ürünleri aktar**'a basın.
  - Sonraki çalışmalarda (seçtiğiniz aralıkta, en sık 15 dk) **stok ve fiyat** güncellenir; ad, açıklama, görsel, marka ve kategori yalnızca ilk eklemede yazılır, sonradan yaptığınız düzenleme ezilmez. Beslemeden kalkan ürünün stoğu sıfırlanır (ürün silinmez).
  - Barkodu elle eklediğiniz bir ürünle ya da başka beslemeyle çakışan ürüne **dokunulmaz**, hata listesinde gösterilir. Beslemeyi silerseniz eklediği ürünler katalogda kalır.
  - Tedarikçi XML'lerindeki küçük bozukluklar dosyayı reddettirmez: kök öğeden önceki artık metin, kaçışsız `&` (örn. `Health & Beauty`, adreslerde `?a=1&b=2`) ve `&nbsp;` gibi tanınmayan varlıklar düzeltilerek okunur. Etiketleri yanlış kapanmış ya da XML olmayan içerik yine anlaşılır bir hata ile reddedilir.
  - Birden çok olası alan varsa (örn. Google Merchant'ta `gtin`, `id`; `price`, `sale_price`) tüm ürünlerde dolu olan alan seçilir: yalnızca bazı ürünlerde bulunan `gtin`/`sale_price` ana alan olmaz.
  - Adres, sunucunun iç ağına bağlanamaz (güvenlik); boyut sınırı 150 MB, en çok 50.000 ürün.

## 5. Pazaryerleri

Bu bölüm yalnızca **Pazaryerleri** kanalı açıkken görünür.

### 5.1 Mağaza bağlama (Mağazalar)
**Mağazalar** > **+ Kanal ekle** ile Trendyol, Hepsiburada, N11 veya Amazon bağlantısı oluşturun. Satıcı bilgileriniz (API anahtarı vb.) şifrelenerek saklanır. Bağlantı "Kimlik hatası" gösteriyorsa bilgileri güncelleyip yeniden bağlayın. Amazon'da yetkilendirme Amazon'un kendi sayfasına yönlendirilerek yapılır.

### 5.2 Eşleme
Pazaryerleri kendi kategori, özellik ve değer listelerini kullanır. **Eşleme** ekranında kendi kategorinizi pazaryerinin kategorisine, özelliklerinizi (ör. Beden, Renk) pazaryerinin özelliklerine ve değerlerini karşılıklarına bağlarsınız, sonra **Eşlemeyi kaydet**'e basarsınız. Ürünü göndermeden önce eşleme tamam olmalıdır; zorunlu özellik eksikse pazaryeri ürünü reddeder.

### 5.3 Ürünü pazaryerine gönderme
Ürünler listesinden ürünü seçip kanala gönderin. Gönderim arka planda yapılır; sonuç ürünün kanal durumunda (yayında, bekliyor, hatalı) görünür. Pazaryerinin görsellere erişebilmesi için sunucuda `Storage:PublicBaseUrl` ayarı gerekir.

### 5.4 Stok ve fiyat
**Stok ve fiyat** ekranında kanal sekmesini seçin (Trendyol, Hepsiburada, N11, Amazon) ve o kanal için stok dağıtımı ile fiyat kurallarını belirleyip **Kuralı kaydet**'e basın. Senkron düzenli aralıklarla çalışır; **Şimdi senkronla** ile elle de başlatabilirsiniz.

### 5.5 Senkron hataları
Kanallarla yapılan işlemlerde oluşan hatalar **Senkron hataları** sayfasında toplanır (Genel bakış'tan da ulaşırsınız). Hata mesajını okuyup nedeni giderin (ör. eksik eşleme, geçersiz fiyat), sonra **Yenile** ile tekrar deneyin.

## 6. Siparişler

**Siparişler** tüm kanallardaki siparişleri tek listede gösterir; her satırda kanal (Trendyol, B2B vb.) yazar. **Siparişleri çek** pazaryeri siparişlerini hemen getirir; ayrıca yeni siparişler webhook ve düzenli senkronla otomatik gelir.

**Pazaryeri siparişi:** Siparişi açıp sırasıyla
1. **Toplamaya al** (Created → Picking),
2. **Faturalandı** olarak bildir (fatura numarası zorunlu; Picking → Invoiced).

Bu durumlar pazaryerine geri yazılır. Pazaryeri reddederse hata panelde görünür ve yerel durum ilerlemez. Kargoya verildi / teslim edildi bilgisi pazaryerinden kendiliğinden gelir; bunları siz ayarlayamazsınız.

**B2B siparişi:** Onay bekliyor → Onayla → Hazırlanıyor → Sevk et (kargo firması ve takip no zorunlu) → Teslim edildi. Reddetme ve iptal stoğu havuza geri ekler. Ayrıntı: B2B rehberi, bölüm 5.

**İadeler** ve **Faturalar** ilgili modüller açıksa menüde görünür.

## 7. B2B bayi sitesi

Bayilerinize özel fiyat, iskonto, minimum sipariş ve koli adedi ile satış yaptığınız ayrı bir sitedir. Kısa akış:

1. **Ayarlar > Modüller**'den Bayi yönetimi ve B2B bayi sitesi açık olmalıdır.
2. Bayi sitede **başvuru** yapar; siz **Bayiler** ekranında onaylar ve bayi grubunu atarsınız.
3. **Fiyat listeleri** ile gruba veya bayiye özel fiyat tanımlarsınız (Excel ile toplu da yüklenebilir).
4. Bayi sepet veya **Hızlı sipariş** (kod + adet listesi, Excel/CSV) ile sipariş verir; siz **Siparişler**'den onaylarsınız.
5. **Cari hesap** bayi düzenleme panelinde izlenir; ödemeleri elle girersiniz.

Hepsinin ayrıntısı [B2B kullanıcı rehberinde](b2b-kullanici-rehberi.md).

## 8. B2C mağaza

Son tüketicilerin kayıtsız gezebildiği herkese açık mağazadır. **Ayarlar > Satış kanalları**'ndan B2C açıldığında kullanılabilir.

### 8.1 Yönetici tarafı
- **Ürünü mağazada gösterme:** Ürünler listesinde ürünleri işaretleyin, **B2C mağazada göster** veya **B2C mağazadan kaldır**'ı seçin. Listede "B2C mağaza" sütunu Görünür/Gizli durumunu gösterir.
- **Kampanya fiyatı:** Ürünleri işaretleyip **B2C kampanya**'yı seçin. Yüzde, sabit fiyat belirleyebilir ya da kampanyayı kaldırabilirsiniz. Kampanyalı üründe mağazada eski fiyat üstü çizili ve indirim rozeti görünür; ürün satırında "B2C kampanya" fiyatı yazar.
- **Kategori gizleme:** **Ayarlar > B2C görünürlük**'te bir kategoriyi işaretlemek, o kategoriyi ve altındaki ürünleri mağazadan gizler.

### 8.2 Müşterinin gördükleri
- **Vitrin:** Ürün listesi, kategori ağacı, arama, marka / fiyat aralığı / "stokta olanlar" süzgeçleri, sıralama ve sayfalama. Mobilde süzgeçler açılır panelde toplanır. Açık ve koyu tema desteklenir.
- **Ürün sayfası:** Görseller, seçenekler, açıklama, kampanya fiyatı. Adresler okunur biçimdedir (`/urun/<ürün-adı>`); arama motorları için sayfa başlığı, açıklama, kanonik adres, Open Graph ve ürün verisi hazırdır.
- **Üyelik:** **Hesap oluştur** (e-posta, parola, üyelik koşulları onayı; kampanya iletisi isteğe bağlı) veya **Google ile devam et** (Google ayarı yapılmışsa). **Giriş yap**'ta **Beni hatırla** seçilebilir.
- **Hesabım:**
  - **Profilim:** ad, iletişim tercihleri, parola değiştirme (Google ile açılan hesapta "Parola belirle").
  - **Adreslerim:** adres ekleme, düzenleme, silme, varsayılan adres seçme (üst sınır vardır).
  - **Siparişlerim:** tüm siparişler, durumları ve sipariş detayı (ürünler, teslimat adresi).

> Sepet ve ödeme adımı henüz yayında değildir; üzerinde çalışılıyor.

## 9. Ayarlar

| Sekme | Ne yapar |
|---|---|
| Satış kanalları | Pazaryerleri, B2B, B2C kanallarını açar/kapatır |
| Modüller | Tek tek modülleri açıp kapatır (kapalı modülün menüsü gizlenir, API'si 403 döner; veri silinmez) |
| B2B görünürlük | Bayi sitesinde ürün/kategori görünürlüğü ve fiyat gösterimi |
| B2C görünürlük | B2C mağazada gizlenecek kategoriler |

Sunucu ayarları (veritabanı, anahtarlar, Google, webhook adresi vb.) README'deki **Ayarlar** tablosundadır.

## 10. Sık sorulanlar

**Menüde bir ekran göremiyorum.** İlgili kanal veya modül kapalıdır. Ayarlar > Satış kanalları / Modüller'den açın.

**Fiyatı değiştirdim ama pazaryerinde eski görünüyor.** Fiyat bir sonraki stok-fiyat senkronunda gider; hemen göndermek için **Şimdi senkronla**.

**Ürün pazaryerinde "hatalı" görünüyor.** Senkron hataları sayfasındaki mesaja bakın; çoğunlukla eksik kategori/özellik eşlemesi ya da geçersiz fiyat/stoktur.

**Mağaza "Kimlik hatası" veriyor.** Satıcı API bilgileri geçersizdir; Mağazalar ekranından doğru bilgilerle yeniden bağlayın.

**Bayi/müşteri e-posta almıyor.** Gerçek e-posta sağlayıcısı bağlanana kadar e-postalar yalnızca sunucu günlüğüne yazılır.

**Bir kanalı kapattım, veriler gitti mi?** Hayır. Kanalı yeniden açınca kaldığı yerden devam eder.

## Bilinen sınırlar
- XML aktarımında varyantlı (iç içe) beslemelerde varyant satırı ürün olarak alınır; ürün adı/açıklaması üst öğeden gelmez. Siparişlerin tedarikçiye iletilmesi henüz yok.
- Dış firma API'leri (Trendyol, Hepsiburada, N11, Amazon, e-Fatura) henüz gerçek hesapla denenmedi; akışlar sahte sunucu ve otomatik testlerle doğrulandı.
- B2B'de online ödeme, kredi limiti ve sevk sonrası iade; B2C'de sepet ve ödeme henüz yok.
