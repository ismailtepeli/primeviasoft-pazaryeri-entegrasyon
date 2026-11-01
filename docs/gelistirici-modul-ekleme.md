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
