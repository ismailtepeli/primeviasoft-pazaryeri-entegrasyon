import { BrowserRouter, Route, Routes } from 'react-router-dom'
import AppLayout from './layout/AppLayout'
import DesignSystem from './pages/DesignSystem'
import Login from './pages/Login'
import Overview from './pages/Overview'
import PagePlaceholder from './pages/PagePlaceholder'
import { AuthProvider } from './auth/AuthContext'
import SyncErrors from './pages/SyncErrors'
import { routes } from './routes'

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/tasarim-sistemi" element={<DesignSystem />} />
          <Route path="/giris" element={<Login />} />
          <Route element={<AppLayout />}>
            {routes.map((r) => (
              <Route key={r.path} path={r.path} element={r.path === '/' ? <Overview /> : <PagePlaceholder name={r.label} />} />
            ))}
            <Route path="/senkron-hatalari" element={<SyncErrors />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}
