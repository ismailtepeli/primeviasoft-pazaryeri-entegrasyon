import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AccountLayout, AddressesPage, OrderDetailPage, OrdersPage, ProfilePage } from './accountPages'
import { LoginPage, RegisterPage, RequireLogin } from './authPages'
import { CartProvider } from './CartContext'
import { CartPage, CheckoutPage, FakePaymentPage, OrderDonePage } from './cartPages'
import { CustomerProvider } from './CustomerContext'
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
        <CustomerProvider>
        <CartProvider>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<ProductsPage />} />
            <Route path="kategori/*" element={<ProductsPage />} />
            <Route path="urun/:slug" element={<ProductDetailPage />} />
            <Route path="sepet" element={<CartPage />} />
            <Route path="odeme" element={<RequireLogin><CheckoutPage /></RequireLogin>} />
            <Route path="odeme/test/:id" element={<RequireLogin><FakePaymentPage /></RequireLogin>} />
            <Route path="siparis-tamam/:id" element={<RequireLogin><OrderDonePage /></RequireLogin>} />
            <Route path="giris" element={<LoginPage />} />
            <Route path="kayit" element={<RegisterPage />} />
            <Route path="hesap" element={<RequireLogin><AccountLayout /></RequireLogin>}>
              <Route index element={<ProfilePage />} />
              <Route path="adresler" element={<AddressesPage />} />
              <Route path="siparisler" element={<OrdersPage />} />
              <Route path="siparisler/:id" element={<OrderDetailPage />} />
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
        </CartProvider>
        </CustomerProvider>
      </ShopProvider>
    </BrowserRouter>
  )
}
