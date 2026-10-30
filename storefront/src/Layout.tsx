import { useEffect, useState, type FormEvent } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useStore } from './StoreContext'

type Theme = 'light' | 'dark'

function initialTheme(): Theme {
  try {
    const saved = localStorage.getItem('theme')
    if (saved === 'light' || saved === 'dark') return saved
  } catch { /* depolama kapalı olabilir */ }
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>(initialTheme)
  useEffect(() => {
    document.documentElement.dataset.theme = theme
    try { localStorage.setItem('theme', theme) } catch { /* depolama kapalı olabilir */ }
  }, [theme])
  return <button type="button" className="store-link-btn" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>{theme === 'dark' ? 'Açık tema' : 'Koyu tema'}</button>
}

/** Sayfa iskeleti: üst çubuk (logo, arama, sepet, hesap), kategori menüsü, içerik, alt bilgi. */
export default function Layout() {
  const { info, user, token, logout } = useStore()
  const navigate = useNavigate()
  const [q, setQ] = useState('')

  function search(e: FormEvent) {
    e.preventDefault()
    navigate(q.trim() ? `/?q=${encodeURIComponent(q.trim())}` : '/')
  }

  useEffect(() => { document.title = info.name }, [info.name])

  return (
    <div className="store">
      <header className="store-header">
        <Link to="/" className="store-brand" aria-label={`${info.name} ana sayfa`}>
          <svg width="32" height="32" viewBox="0 0 64 64" aria-hidden="true">
            <rect width="64" height="64" rx="14" fill="var(--navy-700)" />
            <path d="M18 46V18h14a9 9 0 0 1 0 18H18" fill="none" stroke="var(--amber)" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round" />
            <circle cx="46" cy="46" r="4" fill="var(--amber)" />
          </svg>
          <span className="store-brand__name">{info.name}</span>
        </Link>
        {token && (
          <form className="store-search" role="search" onSubmit={search}>
            <input className="ps-input" type="search" placeholder="Ürün adı ya da barkod ara" aria-label="Ürün ara" value={q} onChange={(e) => setQ(e.target.value)} />
          </form>
        )}
        <nav className="store-account" aria-label="Hesap">
          {token ? (
            <>
              <Link to="/sepet" className="store-link">Sepet (0)</Link>
              <Link to="/siparislerim" className="store-link">Siparişlerim</Link>
              <span className="store-user" title={user?.email}>{user?.fullName || user?.email}</span>
              <button type="button" className="store-link-btn" onClick={logout}>Çıkış yap</button>
            </>
          ) : (
            <>
              <Link to="/giris" className="store-link">Giriş yap</Link>
              <Link to="/basvuru" className="ps-btn">Bayi ol</Link>
            </>
          )}
        </nav>
      </header>
      {token && (
        <nav className="store-categories" aria-label="Kategoriler">
          <NavLink to="/" end className="store-categories__link">Tüm ürünler</NavLink>
        </nav>
      )}
      <main className="store-content"><Outlet /></main>
      <footer className="store-footer">
        <span>© {new Date().getFullYear()} {info.name}</span>
        <ThemeToggle />
      </footer>
    </div>
  )
}
