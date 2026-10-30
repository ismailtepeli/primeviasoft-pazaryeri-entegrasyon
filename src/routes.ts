export interface RouteDef {
  path: string
  label: string
  title: string
  subtitle: string
  action: string
  /** Ekranın ait olduğu modül (ADR 0001); yoksa çekirdek, her zaman görünür. */
  module?: string
}

/** Sol menüde görünmeyen ama başlık/alt başlık taşıyan ekranlar. */
export const hiddenRoutes: RouteDef[] = [
  { path: '/senkron-hatalari', label: 'Senkron hataları', title: 'Senkron hataları', subtitle: 'Kanallarla yapılan senkronlarda oluşan tüm hatalar.', action: 'Yenile' },
]

export const productRoutes: RouteDef[] = [
  { path: '/urunler/yeni', label: 'Yeni ürün', title: 'Yeni ürün', subtitle: 'Ürün bilgilerini girin; görselleri kaydettikten sonra eklersiniz.', action: '' },
  { path: '/urunler/:id', label: 'Ürün', title: 'Ürünü düzenle', subtitle: 'Bilgileri, kategoriyi ve görselleri yönetin.', action: '' },
]

/** Yola karşılık gelen ekran tanımı; parametreli ürün sayfalarını da bulur. */
export function routeFor(pathname: string): RouteDef {
  const exact = [...routes, ...hiddenRoutes, ...productRoutes].find((r) => r.path === pathname)
  if (exact) return exact
  if (/^\/urunler\/[^/]+$/.test(pathname)) return productRoutes[1]
  return routes[0]
}

export const routes: RouteDef[] = [
  { path: '/', label: 'Genel bakış', title: 'Genel bakış', subtitle: 'Mağaza, stok ve sipariş durumunuza tek bakışta ulaşın.', action: 'Şimdi senkronla' },
  { path: '/magazalar', label: 'Mağazalar', title: 'Mağazalar', subtitle: 'Pazaryeri bağlantılarınızı yönetin.', action: '+ Kanal ekle', module: 'marketplace' },
  { path: '/urunler', label: 'Ürünler', title: 'Ürünler', subtitle: 'Kataloğunuzu ve kanal durumlarını izleyin.', action: 'Ürün ekle' },
  { path: '/esleme', label: 'Eşleme', title: 'Eşleme', subtitle: 'Kategori, özellik ve değerleri kanallarla eşleyin.', action: 'Eşlemeyi kaydet', module: 'marketplace' },
  { path: '/stok-fiyat', label: 'Stok ve fiyat', title: 'Stok ve fiyat', subtitle: 'Kanal bazlı stok dağıtımı ve fiyat kurallarını belirleyin.', action: 'Kuralı kaydet', module: 'stock-price' },
  { path: '/bayiler', label: 'Bayiler', title: 'Bayiler', subtitle: 'Bayi başvurularını onaylayın, bayileri ve gruplarını yönetin.', action: '', module: 'dealers' },
  { path: '/fiyat-listeleri', label: 'Fiyat listeleri', title: 'Fiyat listeleri', subtitle: 'Bayi gruplarına ve bayilere özel fiyat, iskonto, minimum sipariş ve koli adedi.', action: '', module: 'price-lists' },
  { path: '/siparisler', label: 'Siparişler', title: 'Siparişler', subtitle: 'Tüm kanallardaki siparişleri tek listede takip edin.', action: 'Siparişleri çek', module: 'orders' },
  { path: '/iadeler', label: 'İadeler', title: 'İadeler', subtitle: 'İade taleplerini görüntüleyin ve yönetin.', action: 'İadeleri çek', module: 'orders' },
  { path: '/faturalar', label: 'Faturalar', title: 'Faturalar', subtitle: 'Kesilen faturaları ve durumlarını izleyin.', action: 'Fatura kes', module: 'invoices' },
  { path: '/raporlar', label: 'Raporlar', title: 'Raporlar', subtitle: 'Satış ve operasyon raporlarını inceleyin.', action: 'Rapor oluştur' },
  { path: '/ayarlar', label: 'Ayarlar', title: 'Ayarlar', subtitle: 'Hangi modüllerin açık olacağını belirleyin.', action: '' },
]
