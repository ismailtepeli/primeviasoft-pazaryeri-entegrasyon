# Geliştirici notları: yeni modül ekleme

Mimari karar için bkz. [ADR 0001](adr/0001-moduler-yapi.md). Bu sayfa pratik adımları verir.

## Adımlar

1. **Kimlik**: `ModuleIds` içine kararlı, küçük harf-tire bir sabit ekle (asla yeniden adlandırma).
2. **Katalog**: `ModuleCatalog` listesine `ModuleDef` ekle (ad, açıklama, `Core`, `DefaultEnabled`, `DependsOn`). Yeni B2B özellikleri varsayılan **kapalı** olmalı.
3. **İş türü**: Yeni bir `SyncJobType` varsa `ModuleCatalog.ModuleFor` içinde modüle bağla; kapalı modülün kuyruktaki işleri iptal edilir.
4. **API**: Denetleyiciye `[RequiresModule(ModuleIds.X)]` ekle. Çekirdek ise `[CoreModule]`. `ModuleCoverageTests` her denetleyicinin birini taşımasını denetler; eklemeyi unutursan test kırılır.
5. **Çok kiracılık**: Yeni varlık `ITenantEntity` uygular; global sorgu filtresi otomatik gelir. Arka plan kodunda `AmbientTenant` kullan.
6. **Ön yüz (panel)**: `src/routes.ts` içine yolu ve modül kimliğini ekle; menü ve yönlendirme buradan beslenir. Modül kapalıyken menüde görünmemeli.
7. **Bayi sitesi**: Bayi uçları `AuthPolicies.Dealer` politikasıyla korunur ve `b2b-store` modülüne bağlıdır.
8. **Migration**: Yeni sütunlarda varsayılanı iş kuralına göre ver (ör. min/koli 1, `IsActive` true); EF'nin 0/false varsayılanına güvenme.

## Test kalıbı

- Servisler doğrudan kurulur: `FixedTenant`/`AmbientTenant`, `RecordingQueue : IJobQueue`, `FixedTime : TimeProvider`. Her test sınıfı kendi LocalDB veritabanını kullanır.
- Modül kapıları için `B2BEndToEndTests.RunFilterAsync` örneğine bak.
- Uçtan uca B2B akışı: `B2BEndToEndTests` (başvuru → onay → fiyat → sepet → sipariş → sevk → cari).

## Çalıştırma ve test

```bash
cd backend && dotnet run --project src/Pazaryeri.Api --launch-profile http   # API :5284
npm run dev                      # panel :5173
cd storefront && npm run dev     # bayi sitesi :5174 (/api -> 5284)
```

Derleme/test öncesi çalışan API'yi durdur (DLL kilidi). Kontroller: `dotnet test tests/Pazaryeri.Tests` (backend), `npx tsc -b` ve `npm test` (kökte ve `storefront/` içinde).

Bayi sitesi ortam değişkeni: `VITE_STORE_BASE_DOMAIN` (alt alan adıyla kiracı çözümü); tanımsızsa `/t/<slug>` yolu kullanılır.

## E-posta ve bağlantı ayarları

Bayi e-postaları `IEmailSender` arayüzüyle gönderilir. Şimdilik `LoggingEmailSender` iletiyi günlüğe yazar; gerçek sağlayıcı (SMTP vb.) için aynı arayüzü uygulayıp `DependencyInjection` içinde değiştirin. E-postalardaki bağlantı şablonları yapılandırmadan gelir (`{slug}` ve `{token}` yer tutucuları):

```json
"Store": {
  "LoginUrl": "https://{slug}.b2b.site/giris",
  "ResetUrl": "https://{slug}.b2b.site/sifre-sifirla?token={token}"
}
```

Tanımsızsa geliştirme adresleri (`http://localhost:5174/t/{slug}/...`) kullanılır. Geliştirmede sıfırlama bağlantısını API günlüğünde "E-posta (gönderilmedi)" satırında bulursunuz.

## Pazaryeri webhook alıcısı (Trendyol)

- **Uç nokta:** `POST /webhooks/trendyol/{tenantId}` (kimliksiz; gizli anahtarla doğrulanır). Kanalın çağrısına hemen 200 döner; asıl sipariş verisi gövdeden değil, zaten idempotent olan **sipariş senkronundan** (`OrderSync`) okunur. Alıcı yalnızca doğrular, olayı kaydeder ve senkronu kuyruğa alır.
- **Doğrulama:** Gizli anahtar `Authorization: Basic` parolası, `Bearer` belirteci ya da `x-api-key` ile gelebilir. Veritabanında yalnızca SHA-256 özeti saklanır (`WebhookEndpoint`); karşılaştırma sabit zamanlıdır. Bilinmeyen kiracı, kayıtsız kanal ve yanlış anahtar aynı 401'i alır.
- **İdempotentlik:** (kanal, paket kimliği, durum) çifti başına tek kayıt (`WebhookEvent`, benzersiz dizin). Kanalın tekrar denemesi `Duplicate` (yine 200) döner ve yeni iş açmaz. Gövde okunamazsa olay kaydedilmez ama senkron yine tetiklenir. Başlamamış bir `OrderSync` varsa yenisi açılmaz; çalışan varsa yenisi açılır.
- **Sınırlar:** Gövde en fazla 64 KB (aşılırsa 413); IP başına dakikada 600 istek. `Siparişler` modülü kapalı kiracıda bildirim yok sayılır (200, iş açılmaz).
- **Kayıt:** `POST /api/webhooks/Trendyol` yeni bir gizli anahtar üretir, kanala kaydeder (`createWebhook`) ve özetini saklar; yeniden kayıt anahtarı değiştirir, kanal reddederse eski anahtar geçerli kalır. `GET /api/webhooks/Trendyol` durumu verir. Kayıt için herkese açık adres gerekir: `Webhooks:PublicBaseUrl` (ör. `https://api.firma.com`); yerelde boştur ve kayıt 409 döner.
- **Doğrulanmadı:** Trendyol'daki `createWebhook` uç noktası ve gövdesi belgeye göre yazıldı, gerçek satıcı hesabıyla denenmedi. Gerçek hesapla ilk kayıtta uç nokta, kimlik doğrulama türü ve durum adları kontrol edilmelidir.


## Bildirimler
Yönetici panelinde (üst çubukta zil) ve bayi sitesinde (üst çubukta "Bildirimler") uygulama içi bildirimler vardır. Kodda tek giriş noktası `INotifier` (`NotifyAdminAsync`, `NotifyDealerAsync`); servisler onu isteğe bağlı yapıcı parametresi olarak alır.

- **En iyi çaba:** Bildirim yazılamazsa (hata ya da yinelenen anahtar) asıl iş etkilenmez; `NotificationService` hatayı yutar ve günlüğe yazar.
- **Yinelenen olay:** `dedupeKey` doluysa (kiracı, anahtar) tekildir; aynı olay yeniden işlense de tek bildirim oluşur. Anahtar `b2b-new:{siparişId}`, `mp-new:{kanal}:{siparişNo}` gibi olaya bağlı kurulur.
- **Kitle:** `Admin` bildirimini kiracının tüm yöneticileri ortak görür (okundu bilgisi ortaktır); `Dealer` bildirimi yalnızca o bayiye aittir ve başka bayinin bildirimi okundu yapılamaz (404).
- **Bağlantı:** Yalnızca uygulama içi göreli adres (`/siparis/{id}`) kabul edilir; dış adres ve `//` ile başlayanlar atılır.
- **Üretilenler:** yeni B2B siparişi, bayinin sipariş iptali, stok yetmediği için verilemeyen sipariş, yeni pazaryeri siparişi ve yeni bayi başvurusu (yöneticiye); sipariş alındı, onaylandı, reddedildi, kargoya verildi, teslim edildi, iptal edildi (bayiye).
- **Panel:** zil rozeti 30 saniyede bir `GET /api/notifications/unread-count` ile yoklanır (sekme gizliyken durur); açılınca son 30 bildirim listelenir. Bayi sitesi için aynı uçlar `api/store/notifications` altındadır.
- **Kapsam dışı:** e-posta ve anlık itme (push) bildirimi yok; e-posta için sağlayıcı kararı #85'te bekliyor. Eski bildirimler şimdilik silinmez.

## Güvenlik notları (#55)
- **Anonim uçlar** (kalıcı bekçi: `SecurityReviewTests`): yönetici girişi, bayi sitesi kimlik doğrulaması (bilgi, başvuru, giriş, parola sıfırlama), Amazon yetkilendirme dönüşü, pazaryeri webhook alıcısı ve yüklenmiş görseller (`/api/images/{guid}`, salt okunur). Yeni anonim uç eklenirse testteki liste bilerek güncellenmelidir; görsel dışındaki her anonim uç hız sınırlıdır.
- **Hız sınırı:** giriş/başvuru/sıfırlama/Amazon dönüşü istemci IP'si başına dakikada 10, bayi sitesi bilgisi 60, webhook 600 istek; aşılınca 429 ve `Retry-After`. Ters vekil arkasında tüm istekler vekil IP'sinden görünür; gerçek istemci IP'si için `Security:TrustForwardedHeaders=true` verin (yalnızca uygulamaya sadece vekil üzerinden ulaşılıyorsa).
- **Kiracı yalıtımı:** `ITenantEntity` olan her tablo otomatik kiracı filtresi taşır. Kiracısız tablolar yalnızca kiracının kendisi ve paylaşımlı kanal kataloğudur (`CatalogSyncRun`, `ChannelAttribute`, `ChannelAttributeValue`, `ChannelBrand`, `ChannelCategory`); katalog yalnızca `ChannelCatalogService` (iş/handler) tarafından yazılır, API yalnızca okur. `IgnoreQueryFilters` yalnızca arka plan işlerinde, giriş aramasında ve anonim görsel sunumunda kullanılır.
- **Yüklemeler:** görsel adı diskte yalnızca `{kiracı Guid}/{görsel Guid}{beyaz liste uzantı}`; istemci dosya adı yola girmez. İçerik imzasıyla doğrulanır; görsel istek boyutu sınırlı, Excel içe aktarma ve fiyat listesi 5 MB.
- **Bağımlılıklar:** `dotnet list package --vulnerable --include-transitive` ve `npm audit --omit=dev` (panel ve bayi sitesi) açık bulmadı (1 Kasım 2026).

## B2C müşteri hesabı (api/shop/auth, api/shop/me) — #97
B2C mağazada son tüketici hesabı: e-posta + parola ve **Google ile kayıt/giriş**, profil, adres defteri ve siparişleri.
- **Ayrı kimlik:** müşteri token'ı kendi hedef kitlesine (`Jwt:CustomerAudience`) ve `principal=customer` kimliğine sahiptir. Yönetim API'si müşteri ve bayi token'ını reddeder, bayi uçları yalnızca bayi, müşteri uçları (`api/shop/me`) yalnızca müşteri token'ını kabul eder (`AuthSetup`, testli). Bayi kullanıcısı (`DealerUser`) ve yönetici (`User`) ile aynı tablo paylaşılmaz; `Customer` e-postası kiracı içinde tekildir, aynı e-posta farklı mağazalarda ayrı hesaptır.
- **Uçlar:** anonim ve hız sınırlı (`dakikada 10`): `GET auth/config`, `POST auth/register`, `auth/login`, `auth/google`; kiracı `slug` ile verilir, B2C mağaza modülü kapalıysa 404. Oturumlu (`api/shop/me`): profil, parola, adresler (en fazla 10, ilk adres varsayılan), siparişler (yalnızca kendi `B2C` kanalı siparişleri; başkasının siparişi/adresi 404).
- **Google ile giriş:** Google Identity Services'in verdiği **ID token** sunucuda doğrulanır (`GoogleIdTokenVerifier`): Google'ın herkese açık anahtarlarıyla RS256 imzası, yayıncı, hedef kitle (bizim istemci kimliğimiz) ve süre; anahtarlar bir saat önbelleğe alınır, anahtar dönüşünde bir kez yenilenir. Sunucuda **sır tutulmaz**, yalnızca istemci kimliği gerekir. Doğrulanmış e-posta aynı e-postayla açılmış hesaba bağlanır (ikinci hesap açılmaz); ilk kez gelen Google kullanıcısından üyelik koşulu kabulü istenir (`terms_required`). Yalnızca Google ile açılmış hesap parolayla girilemez; profilden ilk parola belirlenebilir.
- **Kurulum (Google Cloud):** Google Cloud Console > APIs & Services > Credentials > OAuth client ID (Web application). *Authorized JavaScript origins*: mağazanın adresi (geliştirmede `http://localhost:5176`). Oluşan istemci kimliğini `Google:ClientId` ayarına yazın (ör. `dotnet user-secrets set Google:ClientId <kimlik>` ya da ortam değişkeni `Google__ClientId`). Boşsa "Google ile devam et" düğmesi görünmez. Gerçek Google hesabıyla uçtan uca deneme bu kimlik olmadan yapılamaz; belirteç doğrulaması testlerde gerçek imza ve sahte sertifika uç noktasıyla sınanır.
- **KVKK:** kayıtta üyelik koşulları/aydınlatma metni kabul zamanı (`TermsAcceptedAt`) saklanır, kampanya iletisi izni ayrı ve varsayılan kapalıdır. Aydınlatma metni ve sözleşme sayfalarının kendisi #101'de.
- **Arayüz (`shop/`):** `/giris`, `/kayit` (Google düğmesi ile), `/hesap` (profil, parola), `/hesap/adresler`, `/hesap/siparisler` ve `/hesap/siparisler/:id` (durum adımları, kargo takip no, ürünler, adres). Oturum "Beni hatırla"ya göre localStorage/sessionStorage'da tutulur; `next` dönüş adresi yalnızca uygulama içi yol olabilir.
- **Kapsam dışı (sonraki adımlar):** e-posta doğrulama ve parola sıfırlama e-postası (#100, e-posta sağlayıcısı #85'e bağlı), hesabı silme, müşteri tarafından sipariş iptali/iade talebi (#100), siparişin kendisini oluşturma (sepet/ödeme #98; şimdilik siparişler `seed-demo` ile gelir).

## B2C sepet ve ödeme (api/shop/cart, api/shop/checkout) — #98
- **Sepet istemcide, fiyat sunucuda:** sepet tarayıcıda (mağaza başına localStorage) yalnızca (ürün, adet) olarak tutulur. `POST api/shop/cart/quote` (anonim, `ShopPolicy` hız sınırlı, `slug` ile) satırları sunucuda fiyatlar: kampanyalı birim fiyat, satılabilir stok (stok − güvenlik stoğu), kargo, ücretsiz kargoya kalan tutar, toplam ve açık ödeme yöntemleri. İstemciden fiyat hiçbir yerde kabul edilmez. Görünmeyen/başka kiracının ürünü `unavailable`, yetmeyen stok `stock` sorunu olarak döner; sorunlu satır toplama girmez. En fazla 50 farklı ürün, ürün başına 99 adet.
- **Sipariş (`POST api/shop/checkout`, müşteri oturumu):** teslimat adresi müşterinin kendi adres defterinden seçilir (başkasının adresi 400); sunucu sepeti yeniden fiyatlar, stoğu B2B ile aynı atomik düşümle (`DeductAllOrNothingAsync`, ya hep ya hiç) ayırır ve siparişi `B2C` kanalında `Order` olarak yazar (adres siparişe metin olarak kopyalanır, sonradan adres defteri değişse de sipariş değişmez). Stok yetmezse hiç stok tutulmaz ve kalan adet bildirilir. Yönetici bildirimi: `b2c-order-new`.
- **Ödeme yöntemleri:** ayarlar `B2CSettings` (kiracı başına tek kayıt; yönetim ekranı Ayarlar > B2C mağaza, `api/b2c/settings`). *Havale/EFT:* sipariş `Pending` kalır, müşteri banka bilgisini sipariş sonrası ve sipariş detayında görür; ödemeyi yönetici onaylar (yönetici tarafı #100). *Kapıda ödeme:* isteğe bağlı ücret toplama eklenir, durum `OnDelivery`. *Kart:* yalnızca bir ödeme sağlayıcısı yapılandırıldıysa sunulur ve ayarla açılabilir.
- **Ödeme sağlayıcısı soyutlaması:** `IPaymentProvider` (`Available`, `StartAsync` → ödeme sayfası yolu). Şimdilik yalnızca geliştirme için `FakePaymentProvider` vardır (`Payments:Provider=fake`, yalnız `appsettings.Development.json`'da açık; üretimde boş olmalı). Sahte sağlayıcı müşteriyi mağazadaki `/odeme/test/:id` sayfasına yollar; sonuç `POST api/shop/me/orders/{id}/fake-payment` ile işlenir (sağlayıcı sahte değilse bu uç 404 döner). Gerçek sağlayıcı (iyzico/PayTR/Stripe kararı #98'de bekliyor) eklenirken: `IPaymentProvider`'ı uygulayın, sonucu sağlayıcının **doğrulanmış** geri bildirimiyle (imzalı webhook) `CheckoutService.ConfirmCardAsync` mantığına bağlayın; müşterinin tarayıcıdan kendi ödemesini "başarılı" saymasına izin vermeyin.
- **Süre dolumu ve iptal:** bekleyen kart siparişi 30 dakika içinde ödenmezse `B2CPaymentExpiryJob` (5 dk) siparişi iptal eder (`Expired`) ve stoğu geri ekler; ödeme başarısızsa aynı şekilde `Failed`. Müşteri, hazırlanmaya başlamamış (`New`) ve ödemesi alınmamış siparişini kendisi iptal eder (`POST api/shop/me/orders/{id}/cancel`, stok geri eklenir); ödemesi alınmış sipariş için iptal/iade firmayla yapılır (#100).
- **Bilinen eksikler:** misafir (üyeliksiz) sipariş yok, giriş gerekir; kupon alanı #102; bölge/il bazlı kargo ücreti ve kargo entegratörü (Yurtiçi, MNG vb. API) #99'da yapılmadı; sipariş e-postası ve yönetim ekranında sipariş listesi/sevk düğmeleri #100. Admin sipariş listesinde B2C ödeme durumu henüz gösterilmez: ödemesi beklenen siparişleri hazırlamadan önce `Order.PaymentStatus` kontrol edilmelidir.

## Dropshipping XML beslemeleri (api/feeds)
- **Tanıma (`XmlFeed.Analyze`):** XML bir kez taranır; yol başına sayaç ve örnekler tutulur. Ürün öğesi, tekrarlanan ve ≥3 yaprak alanı olan öğedir; adaylar, rol eşlemesinin puanıyla seçilir (barkod/ad/fiyat 100'er, diğer roller 10'ar; ürün başına birden çok barkod/ad gelen üst öğe −150, böylece varyant satırı seçilir). Alan adları Türkçe karakterleri sadeleştirilmiş eş anlamlı sözlükle (`Synonyms`) rollere eşlenir; fiyatta tedarikçi/bayi fiyatı satış fiyatından önce gelir. Öznitelikler (`@barcode`) ve tekrarlanan alt öğeler (`images/image`) alan sayılır, `g:` gibi ad alanı önekleri atılır. Yeni tedarikçi biçimi için `Synonyms` ve `ImageName` genişletilir; biçim bilinmeden de elle eşleme çalışır.
- **Okuma:** `XmlFeed.ReadItems` akış olarak okur. DTD yok sayılır (`DtdProcessing.Ignore`, `XmlResolver=null`): dış varlık çözülmez, varlık başvurusu geçersiz XML olarak reddedilir (XXE ve "milyar gülen" korumalı). Başındaki boşluk/BOM atlanır; windows-1254 gibi kodlamalar okunur.
- **İndirme (`FeedFetcher`):** adres sunucudan istendiği için SSRF korumalıdır: yalnızca http/https, kullanıcı bilgisi yok, her bağlantı (yönlendirmeler dahil) DNS çözümünden sonra `ConnectCallback`'te denetlenir; loopback, özel ağ, link-local (bulut metadata), CGNAT, çok noktaya yayın ve eşdeğer IPv6 adreslerine bağlanılmaz. Sistem vekili kullanılmaz. Boyut (`ProductFeeds:MaxBytes`, 150 MB) ve süre (120 sn) sınırlıdır, gzip açılır, geçici dosya kapanınca silinir. `ProductFeeds:AllowPrivateNetwork` yalnızca geliştirme/testte açılır; üretimde açılmamalıdır.
- **İçe aktarma (`ProductFeedService.RunAsync`):** barkod = tedarikçi ürün kodu. Yeni ürün: ad, açıklama (düz metne çevrilir), görseller (dış adres), marka, kategori (yol oluşturulur), KDV, stok, maliyet (= besleme fiyatı) ve satış fiyatı (maliyet × (1 + kâr %), KDV hariçse KDV eklenir) yazılır. Mevcut ve **aynı beslemenin** ürünü: yalnızca stok/fiyat (ayara göre). Elle eklenmiş ya da başka beslemenin aynı barkodlu ürünü atlanır ve raporlanır. 500'lük gruplarla kaydedilir; eşzamanlı sipariş stoğu değiştirirse grup bir kez yeniden denenir. Beslemeden kalkan ürünün stoğu sıfırlanır (yalnız hiç ürün okunduysa; boş/tanınmayan yanıt stokları sıfırlamaz, hata verir).
- **Zamanlama:** `ProductFeedSyncJob` 10 dakikada bir, aralığı dolan etkin beslemeleri çalıştırır (en az 15 dk). Çalışan besleme `RunningSince` ile kilitlenir; 30 dk içinde ikinci çalıştırma 409 verir, takılı kalan iş süre sonunda serbest kalır. Sonuç beslemeye yazılır (durum, sayılar, ilk 50 satır hatası).
- **Sınırlar:** varyantlı iç içe beslemelerde (Ürün > Varyasyonlar > Varyasyon) varyant satırı ürün öğesi seçilir; üst öğedeki ad/açıklama o satıra aktarılmaz. Görseller dış adreste kalır (yerel kopya yok). Tedarikçiye sipariş iletme (dropshipping sipariş akışı) bu işte yok.

## B2C kargo yönetimi (api/b2c/carriers, sevk) — #99
- **Firmalar:** `ShippingCarrier` (kiracı başına en çok 20; ad, açık/kapalı, sabit ücret, takip bağlantısı şablonu) ve `ShippingRate` (desi aralığı: "en çok N desi → ücret"). Yönetim: Ayarlar > B2C mağaza > Kargo firmaları, `GET/POST/PUT/DELETE api/b2c/carriers`. Firma tanımlı değilse eski davranış sürer: `B2CSettings.ShippingFee` tek kargo ücreti olarak uygulanır.
- **Ücret hesabı** (`CheckoutService.ShippingOptions`): kuralı olmayan firmada sabit ücret; kuralı olan firmada sepet desisine uyan en küçük aralığın ücreti, en büyük aralığı aşan sepete firma sunulmaz. Ücretsiz kargo eşiği (`FreeShippingThreshold`) tüm firmalar için ortaktır. Sepet desisi = satırların (adet × desi) toplamı; desi ürünün **kanal verisindeki** `DimensionalWeight` değerinden (birden çok kanalda en büyüğü) gelir, girilmemişse 0 sayılır. B2C'ye özel ürün desisi alanı yoktur.
- **Sepet/sipariş:** `POST api/shop/cart/quote` ve `checkout` isteğine `carrierId` eklenir; yanıtta `shippingOptions` ve seçili `carrierId` döner. Seçilen firma sepete uymuyorsa teklifte ilk uygun firmaya düşülür, siparişte 409 verilir. Tüm firmalar kapalı ya da sepete uygun değilse sipariş verilemez. Firma adı siparişe yazılır (`Order.ShipmentCarrier`), firma silinse de sipariş etkilenmez.
- **Sevk:** `POST api/b2c/orders/{id}/ship` (`carrierId` ya da `carrier` metni, `trackingNumber`): yalnızca Yeni/Hazırlanıyor ve ödemesi alınmış ya da kapıda ödemeli sipariş sevk edilir; durum `Shipped` olur, firmanın takip şablonundan `Order.TrackingUrl` üretilir (`{takipNo}` yer tutucusu kaçışlanarak). Havale için önce `POST api/b2c/orders/{id}/approve-payment`. Müşteri takip numarasını ve "Kargom nerede?" bağlantısını sipariş detayında görür (bağlantı yalnızca https ise gösterilir).
- **Eksikler:** sevk/ödeme onayı için yönetim ekranı düğmeleri ve müşteriye e-posta/bildirim #100'e bırakıldı (e-posta sağlayıcısı #85); bölge ücreti ve kargo firması API'leriyle otomatik takip #99'da açık.

## B2C vitrini (api/shop) — #96
Herkese açık, oturumsuz, salt okunur ürün vitrini. Kiracı her istekte `slug` sorgu parametresiyle verilir; `ShopCatalogService.ResolveAsync` kiracının etkin olduğunu ve **B2C mağaza** (`b2c-store`) modülünün açık olduğunu doğrular, değilse 404 döner (modül kapısı kiracıyı oturumdan bilemediği için serviste uygulanır).
- **Uçlar:** `GET api/shop/info`, `categories`, `products` (arama, kategori, marka, fiyat aralığı, stokta olanlar, sıralama, sayfalama), `products/{id}`. IP başına dakikada 240 istek.
- **Görünürlük:** ürünün `B2CVisible` bayrağı açık, kategorisi (ya da üst kategorisi) `B2CHidden`/pasif değil. B2B görünürlüğünden bağımsızdır; bayi fiyat listeleri B2C'de geçerli değildir.
- **Fiyat:** KDV dahil `Price`; `B2CSalePrice` doluysa ve normal fiyattan düşükse vitrinde o geçerlidir, normal fiyat `listPrice` olarak (üstü çizili) döner. Süzme ve sıralama etkin fiyata göre veritabanında yapılır.
- **Stok:** kesin adet sızdırılmaz; yalnızca Stokta var / Az kaldı (5 ve altı) / Tükendi. Güvenlik stoğu düşülür.
- **Yönetici:** `PUT api/b2c/products/visibility`, `PUT api/b2c/products/{id}/sale-price`, `PUT api/b2c/categories/{id}/hidden`; Ürünler listesinde (B2C modülü açıkken) "B2C mağazada göster/kaldır".
- **Durum:** B2C modülü Ayarlar > Satış kanalları'ndan açılabilir. Geliştirmede `seed-demo` demo kiracısında açar. Mağaza arayüzü ayrı bir uygulamadır: `shop/` (Vite + React, geliştirmede http://localhost:5176/t/<slug>; kiracı bayi sitesi gibi alt alan adı ya da `/t/<slug>` yol önekiyle çözülür, `VITE_SHOP_BASE_DOMAIN`). Sayfalar: ürün listesi (kategori ağacı, arama, marka/fiyat/stok süzgeci, sıralama, sayfalama, mobilde süzgeç çekmecesi), kategori sayfaları (`/kategori/giyim/tisort`) ve ürün detayı (görsel galerisi, kampanya fiyatı ve indirim rozeti, varyantlar). Sepet ve hesap sonraki adımlardır (#97, #98).
- **SEO (mağaza arayüzü):** ürün adresleri okunur (`/urun/polo-yaka-tisort-<kimlik>`; eski/adı değişmiş adresler güncel adrese yönlenir), her sayfada başlık, açıklama, kanonik adres, Open Graph ve ürün sayfasında schema.org `Product`/`Offer` verisi bulunur. Arama, süzgeçli, sıralı ve 2. sayfa sonrası listeler `noindex` olur, kanonik adresleri süzgeçsiz sayfadır. Bu etiketler tarayıcıda (JavaScript ile) yazılır; JavaScript çalıştırmayan tarayıcılar için sunucu tarafı üretim (SSR/ön işleme) ve `sitemap.xml` ayrıca gerekir (#101 ile birlikte değerlendirilecek).
- **Yönetici (panel):** Ürünler listesinde "B2C kampanya" (yüzde indirim, sabit fiyat ya da kaldır; uygulamadan önce örnek ürün önizlemesi, kurala uymayanlar atlanıp bildirilir), fiyat altında "B2C kampanya" satırı; Ayarlar > B2C görünürlük sekmesinde kategori gizleme (`GET api/b2c/categories`, `PUT api/b2c/products/sale`).
