import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { ProductDetailPage, ProductsPage } from './catalogPages'
import { CartProvider } from './CartContext'
import { CartPage, OrderDetailPage, OrdersPage } from './orderPages'
import Layout from './Layout'
import { AccountPage, ApplyPage, ForgotPasswordPage, LoginPage, RequireLogin, ResetPasswordPage, StatusPage } from './pages'
import { StoreProvider } from './StoreContext'
import { resolveTenant } from './tenant'

const baseDomain = import.meta.env.VITE_STORE_BASE_DOMAIN as string | undefined

export default function App() {
  const tenant = resolveTenant(window.location.hostname, window.location.pathname, baseDomain)
  if (!tenant) {
    return <StatusPage title="Bayi sitesi adresi bulunamadı" text="Firmanızın bayi sitesi adresinden girin (örn. firma.b2b.site). Geliştirmede /t/firma-kodu adresi kullanılır." />
  }

  return (
    <BrowserRouter basename={tenant.basePath}>
      <StoreProvider tenant={tenant} renderState={(state, retry) => {
        if (state === 'loading') return <StatusPage title="Yükleniyor…" text="Bayi sitesi açılıyor." />
        if (state === 'notfound') return <StatusPage title="Bayi sitesi bulunamadı" text="Bu adreste bir bayi sitesi yok. Adresi kontrol edin." />
        if (state === 'unavailable') return <StatusPage title="Site şu an hizmet dışı" text="Bayi sitesi geçici olarak kapalı. Lütfen daha sonra tekrar deneyin." action={{ label: 'Tekrar dene', onClick: retry }} />
        return <StatusPage title="Site açılamadı" text={state.error} action={{ label: 'Tekrar dene', onClick: retry }} />
      }}>
        <CartProvider>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<RequireLogin><ProductsPage /></RequireLogin>} />
            <Route path="kategori/*" element={<RequireLogin><ProductsPage /></RequireLogin>} />
            <Route path="urun/:id" element={<RequireLogin><ProductDetailPage /></RequireLogin>} />
            <Route path="giris" element={<LoginPage />} />
            <Route path="basvuru" element={<ApplyPage />} />
            <Route path="sifremi-unuttum" element={<ForgotPasswordPage />} />
            <Route path="sifre-sifirla" element={<ResetPasswordPage />} />
            <Route path="hesap" element={<RequireLogin><AccountPage /></RequireLogin>} />
            <Route path="sepet" element={<RequireLogin><CartPage /></RequireLogin>} />
            <Route path="siparislerim" element={<RequireLogin><OrdersPage /></RequireLogin>} />
            <Route path="siparis/:id" element={<RequireLogin><OrderDetailPage /></RequireLogin>} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
        </CartProvider>
      </StoreProvider>
    </BrowserRouter>
  )
}
