import { useEffect, useState, type FormEvent } from 'react'
import { Link, NavLink, Outlet, useNavigate, useSearchParams } from 'react-router-dom'
import { storeApi } from './api'
import { childrenOf, type StoreCategory } from './catalog'
import { useCart } from './CartContext'
import { useStore } from './StoreContext'
import { applyTheme, readPreference, savePreference, themeOptions, type ThemePreference } from './theme'

/** Açık / Koyu / Sistem seçimi; tercih saklanır, varsayılan açıktır. */
function ThemeSwitch() {
  const [preference, setPreference] = useState<ThemePreference>(readPreference)
  useEffect(() => {
    applyTheme(preference)
    savePreference(preference)
    if (preference !== 'system') return
    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const onChange = () => applyTheme('system')
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [preference])
  return (
    <div className="store-theme" role="group" aria-label="Tema">
      {themeOptions.map((o) => (
        <button key={o.value} type="button" className="store-theme__btn" aria-pressed={preference === o.value} onClick={() => setPreference(o.value)}>{o.label}</button>
      ))}
    </div>
  )
}

/** Sayfa iskeleti: üst çubuk (logo, arama, sepet, hesap), kategori menüsü, içerik, alt bilgi. */
export default function Layout() {
  const { info, user, token, logout } = useStore()
  const { cart } = useCart()
  const navigate = useNavigate()
  const [q, setQ] = useState('')
  const [categories, setCategories] = useState<StoreCategory[]>([])
  const [params] = useSearchParams()
  const activeCategory = params.get('category')

  useEffect(() => {
    if (!token) { setCategories([]); return }
    storeApi<StoreCategory[]>('/catalog/categories', {}, token).then(setCategories).catch(() => setCategories([]))
  }, [token])

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
              <Link to="/sepet" className="store-link">Sepet ({cart.itemCount})</Link>
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
          <NavLink to="/" end className={({ isActive }) => `store-categories__link${isActive && !activeCategory ? ' active' : ''}`}>Tüm ürünler</NavLink>
          {childrenOf(categories, null).map((c) => (
            <NavLink key={c.id} to={`/?category=${c.id}`} className={`store-categories__link${activeCategory === c.id ? ' active' : ''}`}>{c.name}</NavLink>
          ))}
        </nav>
      )}
      <main className="store-content"><Outlet context={{ categories }} /></main>
      <footer className="store-footer">
        <span>© {new Date().getFullYear()} {info.name}</span>
        <ThemeSwitch />
      </footer>
    </div>
  )
}
