export interface RouteDef {
  path: string
  label: string
  title: string
  subtitle: string
  action: string
}

/** Sol menüde görünmeyen ama başlık/alt başlık taşıyan ekranlar. */
export const hiddenRoutes: RouteDef[] = [
  { path: '/senkron-hatalari', label: 'Senkron hataları', title: 'Senkron hataları', subtitle: 'Kanallarla yapılan senkronlarda oluşan tüm hatalar.', action: 'Yenile' },
]

export const routes: RouteDef[] = [
  { path: '/', label: 'Genel bakış', title: 'Genel bakış', subtitle: 'Mağaza, stok ve sipariş durumunuza tek bakışta ulaşın.', action: 'Şimdi senkronla' },
  { path: '/magazalar', label: 'Mağazalar', title: 'Mağazalar', subtitle: 'Pazaryeri bağlantılarınızı yönetin.', action: '+ Kanal ekle' },
  { path: '/urunler', label: 'Ürünler', title: 'Ürünler', subtitle: 'Kataloğunuzu ve kanal durumlarını izleyin.', action: 'Ürün ekle' },
  { path: '/esleme', label: 'Eşleme', title: 'Eşleme', subtitle: 'Kategori, özellik ve değerleri kanallarla eşleyin.', action: 'Eşlemeyi kaydet' },
  { path: '/stok-fiyat', label: 'Stok ve fiyat', title: 'Stok ve fiyat', subtitle: 'Kanal bazlı stok dağıtımı ve fiyat kurallarını belirleyin.', action: 'Kuralı kaydet' },
  { path: '/siparisler', label: 'Siparişler', title: 'Siparişler', subtitle: 'Tüm kanallardaki siparişleri tek listede takip edin.', action: 'Siparişleri çek' },
  { path: '/iadeler', label: 'İadeler', title: 'İadeler', subtitle: 'İade taleplerini görüntüleyin ve yönetin.', action: 'İadeleri çek' },
  { path: '/faturalar', label: 'Faturalar', title: 'Faturalar', subtitle: 'Kesilen faturaları ve durumlarını izleyin.', action: 'Fatura kes' },
  { path: '/raporlar', label: 'Raporlar', title: 'Raporlar', subtitle: 'Satış ve operasyon raporlarını inceleyin.', action: 'Rapor oluştur' },
]
