import { Link, Navigate, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { AddChannelButton, Button, SyncNowButton, ThemeToggle } from '../components'
import { routeFor, routes } from '../routes'
import './AppLayout.css'

/** Ekrana özgü başlık aksiyonu; tanımsızsa şimdilik yalnızca etiket gösteren buton. */
function headerAction(path: string, label: string) {
  if (path.startsWith('/urunler/')) return <Link to="/urunler" className="ps-btn ps-btn--secondary">← Ürünlere dön</Link>
  if (path === '/urunler') return <Link to="/urunler/yeni" className="ps-btn ps-btn--primary">Ürün ekle</Link>
  if (path === '/') return <SyncNowButton />
  if (path === '/magazalar') return <AddChannelButton />
  return <Button variant="primary">{label}</Button>
}

export default function AppLayout() {
  const { pathname } = useLocation()
  const { authenticated, logout } = useAuth()
  const current = routeFor(pathname)

  if (!authenticated) return <Navigate to="/giris" replace />

  return (
    <div className="app">
      <aside className="app__sidebar">
        <div className="app__brand">
          <svg width="32" height="32" viewBox="0 0 64 64" aria-hidden="true">
            <rect width="64" height="64" rx="14" fill="var(--navy-700)" />
            <path d="M18 46V18h14a9 9 0 0 1 0 18H18" fill="none" stroke="var(--amber)" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round" />
            <circle cx="46" cy="46" r="4" fill="var(--amber)" />
          </svg>
          <span className="app__brand-text">Primevia<span>Soft</span></span>
        </div>
        <nav className="app__nav" aria-label="Ana menü">
          {routes.map((r) => (
            <NavLink key={r.path} to={r.path} end className="app__nav-link">
              {r.label}
            </NavLink>
          ))}
        </nav>
        <div className="app__footer"><ThemeToggle /><button type="button" className="app__logout" onClick={logout}>Çıkış yap</button></div>
      </aside>
      <div className="app__main">
        <header className="app__header">
          <div>
            <h1>{current.title}</h1>
            <p>{current.subtitle}</p>
          </div>
          {headerAction(current.path, current.action)}
        </header>
        <main className="app__content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
