import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { routes } from '../routes'
import './AppLayout.css'

export default function AppLayout() {
  const { pathname } = useLocation()
  const current = routes.find((r) => r.path === pathname) ?? routes[0]

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
      </aside>
      <div className="app__main">
        <header className="app__header">
          <div>
            <h1>{current.title}</h1>
            <p>{current.subtitle}</p>
          </div>
          <button type="button" className="ps-btn ps-btn--primary">{current.action}</button>
        </header>
        <main className="app__content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
