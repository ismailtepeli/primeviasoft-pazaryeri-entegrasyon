import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import Layout from './Layout'
import { ProductDetailPage, ProductsPage, StatusPage } from './pages'
import { ShopProvider } from './ShopContext'
import { resolveTenant } from './tenant'

const baseDomain = import.meta.env.VITE_SHOP_BASE_DOMAIN as string | undefined

export default function App() {
  const tenant = resolveTenant(window.location.hostname, window.location.pathname, baseDomain)
  if (!tenant) {
    return <StatusPage title="Mağaza adresi bulunamadı" text="Mağazanın adresinden girin (örn. firma.magaza.site). Geliştirmede /t/firma-kodu adresi kullanılır." />
  }

  return (
    <BrowserRouter basename={tenant.basePath}>
      <ShopProvider tenant={tenant} renderState={(problem, retry) => {
        if (problem === 'loading') return <StatusPage title="Yükleniyor…" text="Mağaza açılıyor." />
        if (problem === 'notfound') return <StatusPage title="Mağaza bulunamadı" text="Bu adreste açık bir mağaza yok. Adresi kontrol edin." />
        return <StatusPage title="Mağaza açılamadı" text={problem.error} action={{ label: 'Tekrar dene', onClick: retry }} />
      }}>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<ProductsPage />} />
            <Route path="kategori/*" element={<ProductsPage />} />
            <Route path="urun/:id" element={<ProductDetailPage />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      </ShopProvider>
    </BrowserRouter>
  )
}
