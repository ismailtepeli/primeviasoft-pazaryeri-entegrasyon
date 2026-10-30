import { BrowserRouter, Route, Routes } from 'react-router-dom'
import AppLayout from './layout/AppLayout'
import DesignSystem from './pages/DesignSystem'
import Login from './pages/Login'
import Mapping from './pages/Mapping'
import Dealers from './pages/Dealers'
import PriceLists from './pages/PriceLists'
import Orders from './pages/Orders'
import Overview from './pages/Overview'
import PagePlaceholder from './pages/PagePlaceholder'
import { AuthProvider } from './auth/AuthContext'
import { ModulesProvider } from './modules/ModulesContext'
import Settings from './pages/Settings'
import ProductEdit from './pages/ProductEdit'
import Products from './pages/Products'
import StockPrice from './pages/StockPrice'
import Stores from './pages/Stores'
import SyncErrors from './pages/SyncErrors'
import { routes } from './routes'

function pageFor(path: string, label: string) {
  if (path === '/') return <Overview />
  if (path === '/magazalar') return <Stores />
  if (path === '/urunler') return <Products />
  if (path === '/esleme') return <Mapping />
  if (path === '/stok-fiyat') return <StockPrice />
  if (path === '/siparisler') return <Orders />
  if (path === '/ayarlar') return <Settings />
  if (path === '/bayiler') return <Dealers />
  if (path === '/fiyat-listeleri') return <PriceLists />
  return <PagePlaceholder name={label} />
}

export default function App() {
  return (
    <AuthProvider>
      <ModulesProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/tasarim-sistemi" element={<DesignSystem />} />
          <Route path="/giris" element={<Login />} />
          <Route element={<AppLayout />}>
            {routes.map((r) => (
              <Route key={r.path} path={r.path} element={pageFor(r.path, r.label)} />
            ))}
            <Route path="/senkron-hatalari" element={<SyncErrors />} />
            <Route path="/urunler/yeni" element={<ProductEdit />} />
            <Route path="/urunler/:id" element={<ProductEdit />} />
          </Route>
        </Routes>
      </BrowserRouter>
      </ModulesProvider>
    </AuthProvider>
  )
}
