# Bayi sitesi (storefront)

B2B bayilerinin giriş yapıp ürünleri gördüğü, yönetim panelinden (`../src`) ayrı web uygulaması. Aynı API'yi `api/store/...` alanından kullanır; bayi token'ı yönetim API'sine giremez (ADR 0001).

```bash
npm install
npm run dev     # http://localhost:5174, /api -> http://localhost:5284
npm test
npm run build
```

## Kiracı çözümleme

Site hangi firmaya ait olduğunu adresten bulur (`src/tenant.ts`):

1. **Alt alan adı:** `firma.b2b.site`. Taban alan adı `VITE_STORE_BASE_DOMAIN` ile verilir (örn. `b2b.site`).
2. **Yol öneki (geliştirme/yedek):** `http://localhost:5174/t/demo`. `demo`, kiracının `Slug` değeridir (yerel seed: `demo`).

Özel alan adı desteği sonra eklenecek (#48 alan adı kararı).

## Hizmet dışı

Açılışta `GET api/store/auth/info?slug=` çağrılır. Bayi yönetimi ve B2B bayi sitesi modüllerinden biri kapalıysa "Site şu an hizmet dışı" sayfası gösterilir; slug bilinmiyorsa "bulunamadı".

## Güvenlik notları

- Anonim uçlar (`info`, `apply`, `login`) istemci IP'si başına dakikada sınırlıdır (429). Ters vekil arkasında gerçek IP için `ForwardedHeaders` yapılandırılmalıdır.
- Başvuruda e-posta doğrulaması ve CAPTCHA henüz yok; parola sıfırlama için #77'ye bakın.
