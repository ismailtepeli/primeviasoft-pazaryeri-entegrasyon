# Pazaryeri sandbox (sahte Trendyol)

Trendyol test ortamı vermediği için `backend/tools/Pazaryeri.Sandbox`, entegrasyon API'sini dokümana göre taklit eder.
İstemci kodunu paylaşmaz; bu yüzden istemcinin sözleşmeyi yanlış anlaması testlerde yakalanır.

## Çalıştırma

```bash
dotnet run --project backend/tools/Pazaryeri.Sandbox   # http://localhost:5099
```

API'yi sandbox'a yönlendirmek için (yalnızca sunucu yapılandırması; mağaza ayarından değiştirilemez):

```json
"Marketplace": { "Sandbox": { "Trendyol": "http://localhost:5099" } }
```

Ortam değişkeni olarak: `Marketplace__Sandbox__Trendyol=http://localhost:5099`. Bu ayar boşken gerçek adresler kullanılır.
Üretimde bu ayar tanımlanmamalıdır.

## Neleri taklit eder

- Kimlik: `Basic apiKey:apiSecret` yoksa 401, `User-Agent` yoksa 403.
- Katalog: kategori ağacı (yalnızca 11, 12, 21 son kategori), 2.500 marka (1000'lik sayfalar), kategori özellikleri (Beden ve Renk zorunlu).
- Ürün gönderimi: doğrulama (ad ≤ 100 karakter, satış ≤ liste fiyatı, stok 0-20000, KDV oranı, görsel, zorunlu özellikler) ve batch sonucu.
- Stok/fiyat: bilinmeyen barkod, sınır ihlali kalem bazında `FAILED` döner.
- Siparişler: sayfalama, `status`, `startDate`, `endDate`, `lastModifiedStartDate` süzgeçleri.
- Paket durumu: `Created → Picking → Invoiced → Shipped → Delivered`; geçersiz geçiş 400, `Invoiced` için `invoiceNumber` zorunlu.
- Webhook kaydı: yalnızca `https`, `BASIC_AUTHENTICATION`, en fazla 15 kayıt.

## Yönetim uçları

| Uç | Gövde | İş |
|---|---|---|
| `POST /__sandbox/orders` | `{count, barcode, status, price}` | Sipariş ekler |
| `POST /__sandbox/fail` | `{count, status, retryAfterSeconds}` | Sıradaki istekleri başarısız eder (429, 500...) |
| `POST /__sandbox/pending-polls` | `{count}` | Yeni batch'ler önce `IN_PROGRESS` döner |
| `POST /__sandbox/webhooks/send` | `{packageId, url, username, password}` | Paket JSON'unu Basic kimlikle alıcıya gönderir |
| `GET /__sandbox/state` | | Ürünler, siparişler, webhook'lar, gelen istekler |
| `POST /__sandbox/reset` | | Her şeyi sıfırlar |

## Testler

`backend/tests/Pazaryeri.Tests/TrendyolSandboxTests.cs` sandbox'ı süreç içinde rastgele portta açar ve `TrendyolClient`'ı gerçek HTTP ile sınar.

## Sınır

Sandbox, dokümanı doğru anladığımızı gösterir; Trendyol'un gerçekte nasıl davrandığını göstermez.
Gerçek hesapla ilk temasta (önce salt okunur) çıkan farklar sandbox'a ve teste eklenmelidir.
