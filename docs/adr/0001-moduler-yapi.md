# ADR 0001: Modüler yapı (B2B dönüşümü)

- Durum: Kabul edildi (öneri; itiraz gelirse güncellenir)
- Tarih: 2026-10-30
- İlgili görevler: #60 (ana görev), #61 (bu karar), #62 (backend altyapı), #63 (Ayarlar > Modüller), #64 (mevcut ekranlar)

## Bağlam

Proje pazaryeri entegrasyon panelidir. B2B platformuna dönüşürken bu panel yönetim paneli olarak kalacak, bayiler için ayrı bir site (storefront) eklenecek ve menüler/özellikler **Ayarlar > Modüller** sayfasından açılıp kapanacak. Her kiracı yalnızca ihtiyacı olan parçaları görmeli (yalnızca B2B, yalnızca pazaryeri ya da ikisi birden).

## Karar

### 1. Modül nedir

Modül, birlikte açılıp kapanan bir özellik grubudur: menü girdileri + API uç noktaları + arka plan işleri + (varsa) ayarları. Veri modeli (tablolar) modüle bağlı **değildir**: modül kapanınca veri silinmez, yalnızca erişim ve işler durur.

Kod içinde tek yerde tanımlanır (`ModuleCatalog`, Application katmanı):

| Alan | Anlam |
|---|---|
| `Id` | Kararlı kimlik, küçük harf-tire (`marketplace`, `b2b-store`). Asla yeniden adlandırılmaz. |
| `Name`, `Description` | Ayarlar ekranında gösterilen Türkçe ad ve açıklama |
| `Kind` | `Core` (kapatılamaz) ya da `Optional` |
| `DefaultEnabled` | Kiracı için kayıt yoksa durum |
| `DependsOn` | Açık olması gereken diğer modüller |
| `Routes` | Ön yüz yolları (menü ve yönlendirme için) |
| `Controllers` | Koruma altındaki API denetleyicileri (`[RequiresModule("id")]`) |
| `Jobs` | Kapalıyken atlanacak zamanlanmış işler / `SyncJobType` değerleri |

### 2. Modül listesi (ilk sürüm)

| Id | Ad | Tür | Bağımlılık | Bugünkü ekran / denetleyici |
|---|---|---|---|---|
| `core` | Çekirdek | Core | — | Genel bakış, Ürünler, Kategoriler, görseller, Ürün içe aktarma, stok havuzu ve stok hareketleri, Ayarlar, Auth |
| `marketplace` | Pazaryeri entegrasyonu | Optional | core | Mağazalar, Eşleme (kategori/özellik), kanal kimlik bilgileri, katalog senkronu, ürün gönderimi, Amazon OAuth, `SyncJobs`, `SyncNow` |
| `stock-price` | Stok ve fiyat senkronu | Optional | marketplace | Stok-fiyat ekranı: kanal stok kuralları, fiyat kuralları, delta senkron (`StockPriceSync`) |
| `orders` | Siparişler | Optional | core | Siparişler, İadeler, sipariş senkronu (`OrderSync`), webhook alıcısı; B2B siparişleri de burada görünür |
| `invoices` | Faturalar | Optional | orders | Faturalar, e-Fatura gönderimi (`InvoiceSend`) |
| `dealers` | Bayi yönetimi | Optional | core | Bayiler, bayi grupları (#65) |
| `price-lists` | Fiyat listeleri | Optional | dealers | Bayi fiyat listeleri, iskonto, minimum/koli (#66) |
| `b2b-store` | B2B bayi sitesi | Optional | dealers, orders | Storefront ve `api/store/...` (#68–#70); ürün görünürlüğü ayarları (#67) |

Notlar:
- Stok havuzu çekirdektedir: B2B de pazaryeri de aynı havuzdan düşer. Pazaryeri kapalı olsa bile çalışır.
- `price-lists` kapalıyken bayiler ürünün genel satış fiyatını görür.
- Raporlar ekranı şimdilik yer tutucu; hazır olduğunda `reports` modülü olarak eklenir.

### 3. Kiracı başına durum

`TenantModule` tablosu: `TenantId`, `ModuleId`, `Enabled`, `SettingsJson`, `UpdatedAt`. Kayıt yoksa `DefaultEnabled` geçerlidir. Mevcut kiracılar için başlangıçta `core`, `marketplace`, `stock-price`, `orders`, `invoices` açık, B2B modülleri kapalı olur; böylece bugünkü kullanım değişmez.

Durum önbelleklenir (kiracı başına kısa süreli); aç/kapa işlemi önbelleği geçersiz kılar.

### 4. Aç/kapa kuralları

- `Core` modül kapatılamaz.
- Bir modülü açmak için `DependsOn` listesi açık olmalı; değilse API 409 ve eksik modülleri döner (ekran bunları tek tıkla birlikte açmayı önerir).
- Bir modülü kapatmak, ona bağımlı açık modül varsa reddedilir (409, bağımlıların listesiyle).
- Kapalı modülün API uç noktaları **403** (`{ "error": "module_disabled", "module": "id" }`) döner; ekran bunu "Bu modül kapalı, Ayarlar'dan açın" sayfasına çevirir. Kimlik doğrulama hatasından ayrılabilmesi için 404 yerine 403 seçildi.
- Kapalı modülün menü girdileri görünmez; adresine gidilirse bilgilendirme sayfası açılır.
- Kapalı modülün zamanlanmış işleri kiracıyı atlar; kuyruktaki `SyncJob` işleri çalıştırılmaz, "modül kapalı" hatasıyla Failed olmaz, beklemede kalmaz (iş reddedilerek iptal edilir ve kayıt düşülür).
- Verisi silinmez; modül yeniden açılınca kaldığı yerden devam eder (ör. sipariş senkron imleci).

### 5. Ayarlar

Modüle özgü ayarlar `SettingsJson` içinde, modülün kendi tipli sınıfıyla saklanır ve sürüm alanı taşır. Ortak ayarlar (firma bilgisi vb.) ayrı bir "Hesap" sekmesinde olur; bu ADR kapsamı dışında.

### 6. Mimari: tek API, tek veritabanı

- B2B storefront ve yönetim paneli **aynı API'yi ve aynı veritabanını** kullanır. Ayrı servis ya da ayrı okuma modeli şimdilik yok (karmaşıklık, tutarsızlık riski; stok havuzu tek doğru kaynak olmalı).
- Storefront için ayrı yol alanı: `api/store/...`; yönetim panelinin `api/...` uç noktalarından ayrı yetkilendirme (bayi JWT'si, ayrı hedef kitle) ve ayrı hız sınırı.
- Yük ya da güvenlik gerektirirse `api/store` ileride ayrı bir süreç olarak dağıtılabilir; kod tek çözümde, ayrı klasörlerde tutulur (`storefront/` ön yüz, `Pazaryeri.Api` içinde `Store` denetleyicileri).
- Kiracı çözümleme storefront'ta alan adı/yol önekinden yapılır (#48 kararına bağlı); yönetim panelinde mevcut JWT'deki kiracı kullanılır.

### 7. Ön yüz

`routes.ts` her yola `module` alanı ekler. Menü ve yönlendirme, açık modüller listesinden (`GET api/modules`) süzülür; liste uygulama açılışında yüklenir ve Ayarlar'da değişince yenilenir. `core` yolları her zaman görünür.

## Sonuçlar

Olumlu:
- Her kiracı yalnızca kullandığı parçaları görür; yeni modül eklemek katalog girdisi + `[RequiresModule]` + `routes.ts` alanı kadardır.
- Mevcut kod taşınmaz; yalnızca etiketlenir (#64), bu yüzden risk düşüktür.

Olumsuz / dikkat:
- Her yeni uç nokta bir modüle atanmayı gerektirir; atanmayan uç nokta korumasız kalır. Önlem: test, `ApiController` sınıflarının hepsinde `[RequiresModule]` ya da `[CoreModule]` işareti arar.
- Modüller arası bağımlılık döngüsü yasaktır; katalog testle doğrulanır.
- Modül kapanınca kuyruktaki işlerin davranışı (iptal) dikkatle test edilmelidir (#62).

## Reddedilen seçenekler

- **Modülü ayrı derleme/eklenti (plugin) yapmak:** Bu ölçek için gereksiz karmaşıklık; tek çözüm içinde mantıksal modül yeterli.
- **404 dönmek:** Kapalı modülle yetkisiz/yanlış adres ayırt edilemez, destek zorlaşır.
- **Storefront için ayrı veritabanı/okuma modeli:** Stok ve fiyat tutarlılığı zorlaşır; ihtiyaç doğarsa önbellekle çözülür.

## Uygulama sırası

#62 (katalog, `TenantModule`, `GET/PUT api/modules`, `[RequiresModule]`, iş atlama) → #63 (Ayarlar > Modüller, dinamik menü) → #64 (mevcut ekranları modüllere atama).
