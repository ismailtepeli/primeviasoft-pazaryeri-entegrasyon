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
