import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { ClipboardList, LayoutGrid, LogOut, Menu, Monitor, Moon, Search, ShoppingCart, Sun, User, X } from 'lucide-react'
import { storeApi } from './api'
import { categoryHref, childrenOf, type StoreCategory } from './catalog'
import { useCart } from './CartContext'
import { useStore } from './StoreContext'
import { applyTheme, readPreference, savePreference, themeOptions, type ThemePreference } from './theme'

export interface CatalogContext { categories: StoreCategory[]; loaded: boolean }

const themeIcons = { light: Sun, dark: Moon, system: Monitor } as const

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
      {themeOptions.map((o) => {
        const Icon = themeIcons[o.value]
        return (
          <button key={o.value} type="button" className="store-theme__btn" aria-pressed={preference === o.value} onClick={() => setPreference(o.value)}>
            <Icon size={14} aria-hidden /> {o.label}
          </button>
        )
      })}
    </div>
  )
}

/** "Tüm kategoriler" paneli: ana kategoriler ve alt kategorileri sütunlarda (mega menü); dar ekranda ana kategoriler üstte, alt kategoriler altta. */
function CategoryMenu({ categories, onClose }: { categories: StoreCategory[]; onClose: () => void }) {
  const roots = childrenOf(categories, null)
  const [active, setActive] = useState<string | undefined>(roots[0]?.id)
  const current = roots.find((r) => r.id === active) ?? roots[0]
  return (
    <div className="mega" role="dialog" aria-label="Tüm kategoriler">
      <div className="mega__inner">
        <ul className="mega__roots">
          {roots.map((r) => (
            <li key={r.id}>
              <Link to={categoryHref(r.slugPath)} className={`mega__root${current?.id === r.id ? ' mega__root--active' : ''}`} onMouseEnter={() => setActive(r.id)} onFocus={() => setActive(r.id)} onClick={onClose}>
                <span>{r.name}</span><span className="store-note">{r.productCount}</span>
              </Link>
            </li>
          ))}
        </ul>
        {current && (
          <div className="mega__panel">
            <Link to={categoryHref(current.slugPath)} className="mega__title" onClick={onClose}>{current.name} kategorisinin tümü</Link>
            <ul className="mega__cols">
              {childrenOf(categories, current.id).map((c) => (
                <li key={c.id}>
                  <Link to={categoryHref(c.slugPath)} className="mega__sub" onClick={onClose}>{c.name} <span className="store-note">({c.productCount})</span></Link>
                  <ul>
                    {childrenOf(categories, c.id).map((g) => <li key={g.id}><Link to={categoryHref(g.slugPath)} className="mega__leaf" onClick={onClose}>{g.name}</Link></li>)}
                  </ul>
                </li>
              ))}
              {childrenOf(categories, current.id).length === 0 && <li className="store-note">Bu kategoride alt kategori yok.</li>}
            </ul>
          </div>
        )}
      </div>
    </div>
  )
}

/** Sayfa iskeleti: üst çubuk (logo, arama, sepet, hesap), kategori çubuğu ve mega menü, içerik, alt bilgi. */
export default function Layout() {
  const { info, user, token, logout } = useStore()
  const { cart } = useCart()
  const navigate = useNavigate()
  const [q, setQ] = useState('')
  const [categories, setCategories] = useState<StoreCategory[]>([])
  const [loaded, setLoaded] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!token) { setCategories([]); setLoaded(false); return }
    storeApi<StoreCategory[]>('/catalog/categories', {}, token).then(setCategories).catch(() => setCategories([])).finally(() => setLoaded(true))
  }, [token])

  useEffect(() => { document.title = info.name }, [info.name])

  useEffect(() => {
    if (!menuOpen) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setMenuOpen(false) }
    const onClick = (e: MouseEvent) => { if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false) }
    document.addEventListener('keydown', onKey)
    document.addEventListener('mousedown', onClick)
    return () => { document.removeEventListener('keydown', onKey); document.removeEventListener('mousedown', onClick) }
  }, [menuOpen])

  function search(e: FormEvent) {
    e.preventDefault()
    navigate(q.trim() ? `/?q=${encodeURIComponent(q.trim())}` : '/')
  }

  const roots = childrenOf(categories, null)
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
            <Search size={18} aria-hidden className="store-search__icon" />
            <input className="ps-input" type="search" placeholder="Ürün adı, marka ya da barkod ara" aria-label="Ürün ara" value={q} onChange={(e) => setQ(e.target.value)} />
          </form>
        )}
        <nav className="store-account" aria-label="Hesap">
          {token ? (
            <>
              <Link to="/siparislerim" className="store-action"><ClipboardList size={20} aria-hidden /><span>Siparişlerim</span></Link>
              <Link to="/sepet" className="store-action" aria-label={`Sepet, ${cart.itemCount} adet`}>
                <span className="store-action__icon"><ShoppingCart size={20} aria-hidden />{cart.itemCount > 0 && <span className="store-badge">{cart.itemCount > 99 ? '99+' : cart.itemCount}</span>}</span>
                <span>Sepet</span>
              </Link>
              <Link to="/hesap" className="store-action" title={user?.email}><User size={20} aria-hidden /><span className="store-user">{user?.fullName || user?.email}</span></Link>
              <button type="button" className="store-action store-action--btn" onClick={logout}><LogOut size={20} aria-hidden /><span>Çıkış</span></button>
            </>
          ) : (
            <>
              <Link to="/giris" className="store-action"><User size={20} aria-hidden /><span>Giriş yap</span></Link>
              <Link to="/basvuru" className="ps-btn">Bayi ol</Link>
            </>
          )}
        </nav>
      </header>
      {token && (
        <div className="store-catbar" ref={menuRef}>
          <button type="button" className="store-catbar__all" aria-expanded={menuOpen} onClick={() => setMenuOpen((o) => !o)}>
            {menuOpen ? <X size={18} aria-hidden /> : <Menu size={18} aria-hidden />} Tüm kategoriler
          </button>
          <nav className="store-categories" aria-label="Kategoriler">
            <NavLink to="/" end className={({ isActive }) => `store-categories__link${isActive ? ' active' : ''}`}><LayoutGrid size={16} aria-hidden /> Tüm ürünler</NavLink>
            {roots.slice(0, 8).map((c) => (
              <NavLink key={c.id} to={categoryHref(c.slugPath)} className={({ isActive }) => `store-categories__link${isActive ? ' active' : ''}`}>{c.name}</NavLink>
            ))}
          </nav>
          {menuOpen && <CategoryMenu categories={categories} onClose={() => setMenuOpen(false)} />}
        </div>
      )}
      <main className="store-content"><Outlet context={{ categories, loaded } satisfies CatalogContext} /></main>
      <footer className="store-footer">
        <span>© {new Date().getFullYear()} {info.name}</span>
        <ThemeSwitch />
      </footer>
    </div>
  )
}
