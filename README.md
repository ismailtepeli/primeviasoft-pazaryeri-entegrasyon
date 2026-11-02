# Pazaryeri Entegrasyon

Çok kiracılı satış yönetimi: pazaryerleri (Trendyol, Hepsiburada, N11, Amazon), B2B bayi sitesi ve (yakında) B2C mağaza. Satış kanalları ilk girişte kurulum sihirbazından, sonra Ayarlar > Satış kanalları'ndan açılıp kapatılır.

## Dokümantasyon

- [Kullanım kılavuzu](docs/kullanim-kilavuzu.md)
- [B2B kullanıcı rehberi](docs/b2b-kullanici-rehberi.md)
- [Yeni modül ekleme, güvenlik ve bildirim notları (geliştirici)](docs/gelistirici-modul-ekleme.md)
- [ADR 0001: Modüler yapı](docs/adr/0001-moduler-yapi.md)

## Geliştirme ortamı

Gerekenler: Node.js, .NET 10 SDK, SQL Server LocalDB (Windows) ve Docker Desktop.

```bash
# 1. RabbitMQ (iş kuyruğu) ve Redis (hız sınırı) — Docker Desktop açık olmalı
docker compose up -d

# 2. API (http://localhost:5284); açılışta veritabanını oluşturur/günceller ve tohum kullanıcıyı ekler
cd backend && dotnet run --project src/Pazaryeri.Api --launch-profile http

# 3. Yönetici paneli (http://localhost:5173), bayi sitesi (http://localhost:5174/t/demo) ve B2C mağaza (http://localhost:5176/t/demo)
npm install && npm run dev
npm --prefix storefront install && npm --prefix storefront run dev
npm --prefix shop install && npm --prefix shop run dev
```

Vite sunucuları `/api` isteklerini API'ye yönlendirir; API kapalıysa 502 görürsünüz.

### Giriş bilgileri (yalnızca Development)
`appsettings.Development.json` içindeki `Seed` bölümü: yönetici `admin@demo.local`, kiracı kısaltması `demo` (bayi sitesi adresindeki `/t/demo`). Bu bölüm yalnızca geliştirme ayarındadır; `appsettings.json` (üretim) tohum bilgisi içermez ve tohumlama yalnızca Development ortamında çalışır.

### Demo veri
Tekrarlanabilir ve güvenle yeniden çalıştırılabilir: ne eksikse ekler, var olana dokunmaz.

```bash
cd backend && dotnet run --project src/Pazaryeri.Api -- seed-demo
```

Dört kategori, `DEMO-` barkodlu on ürün (B2B ve B2C'de görünür, biri kampanyalı), bir bayi grubu, bir bayi (`bayi@demo.local` / `Seed:DemoDealerPassword`) ve bayi sitesi ile B2C mağaza modüllerini hazırlar. B2C demo müşterisi: `musteri@demo.local` / `Seed:DemoCustomerPassword` (adres defteri ve üç sipariş: teslim edildi, hazırlanıyor, yeni). Sıfırdan başlamak için veritabanını silin; API açılışta yeniden kurar:

```bash
cd backend && dotnet ef database drop -f -p src/Pazaryeri.Infrastructure -s src/Pazaryeri.Api
```

### Ayarlar
| Ayar | Ne için | Not |
|---|---|---|
| `ConnectionStrings:Default` | Veritabanı | Varsayılan LocalDB `Pazaryeri` |
| `Jwt:Key` | Oturum imzası (en az 32 karakter) | Üretimde user-secrets/ortam değişkeni |
| `Credentials:Key` | Kanal API bilgilerini şifreleyen anahtar (32 bayt, Base64) | Geliştirmede sabit; üretimde değiştirin ve saklayın, kaybolursa kayıtlı kanal bilgileri çözülemez |
| `RabbitMq:*`, `Redis:ConnectionString` | İş kuyruğu ve hız sınırı | `docker compose up -d` ile gelir |
| `Storage:ImagesPath` | Yüklenen görsellerin klasörü | Boşsa uygulama klasörü altı |
| `Storage:PublicBaseUrl` | Pazaryerlerinin görseli çekeceği herkese açık adres | Yerelde boş; pazaryerine ürün göndermek için gerekir |
| `Amazon:Lwa:*` | Amazon yetkilendirme (istemci kimliği, sırrı, uygulama kimliği, dönüş adresi) | Boşsa Amazon bağlanamaz |
| `Payments:Provider` | B2C kart ödemesi sağlayıcısı | Yalnızca geliştirmede `fake` (deneme sayfası, gerçek para geçmez); üretimde boş bırakın, kart ödemesi kapalı kalır |
| `Google:ClientId` | B2C mağazada "Google ile devam et" için OAuth web istemci kimliği | Boşsa düğme görünmez; kurulum için geliştirici notlarına bakın |
| `Webhooks:PublicBaseUrl` | Pazaryeri webhook kaydı için herkese açık API adresi | Yerelde boş; kayıt 409 döner |
| `Store:LoginUrl`, `Store:ResetUrl` | Bayi e-postalarındaki bağlantılar | Bkz. B2B rehberi |
| `Security:TrustForwardedHeaders` | Ters vekil arkasında gerçek istemci IP'si | Yalnızca uygulamaya sadece vekil üzerinden ulaşılıyorsa |

### Testler
```bash
cd backend && dotnet test tests/Pazaryeri.Tests   # önce docker compose up -d (RabbitMQ/Redis testleri için)
npx tsc -b && npm test                            # yönetici paneli
cd storefront && npx tsc -b && npm test           # bayi sitesi
cd shop && npx tsc -b && npm test                 # B2C mağaza
```
Backend testlerini çalıştırmadan önce çalışan API'yi kapatın (derleme çıktısını kilitler). Docker kapalıysa yalnızca `QueueIntegrationTests` ve `RateLimiterTests` düşer.

### Gerçek bilgiyle yapılacaklar
Dev veritabanındaki Trendyol bağlantısı sahte bilgiyle "Kimlik hatası" durumundadır; gerçek satıcı bilgisi alınınca Mağazalar ekranından yeniden bağlanır. Dış firma API'leri (Trendyol, Hepsiburada, N11, Amazon, e-Fatura) geliştirmeye henüz açık değildir; ilgili akışlar yalnızca sahte sunucu ve otomatik testlerle doğrulanmıştır.
