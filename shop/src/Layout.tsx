import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { LayoutGrid, LogIn, Menu, Monitor, Moon, Package, Search, ShoppingCart, Sun, User, UserPlus, X } from 'lucide-react'
import { useCart } from './CartContext'
import { useCustomer } from './CustomerContext'
import { categoryHref, childrenOf, type ShopCategory } from './shop'
import { useShop } from './ShopContext'
import { applyTheme, readPreference, savePreference, themeOptions, type ThemePreference } from './theme'

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

/** "Tüm kategoriler" paneli: ana kategoriler ve alt kategorileri sütunlarda; dar ekranda ana kategoriler üstte, alt kategoriler altta. */
function CategoryMenu({ categories, onClose }: { categories: ShopCategory[]; onClose: () => void }) {
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

/** Sayfa iskeleti: üst çubuk (logo, arama), kategori çubuğu ve mega menü, içerik, alt bilgi. Sepet ve hesap sonraki adımlarda (#97, #98). */
export default function Layout({ children }: { children?: ReactNode }) {
  const { info, categories } = useShop()
  const { token, profile } = useCustomer()
  const { count } = useCart()
  const navigate = useNavigate()
  const [q, setQ] = useState('')
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)

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
          {import.meta.env.DEV && <span className="dev-badge dev-badge--b2c" title="Yalnızca geliştirme sunucusunda görünür">B2C mağaza</span>}
        </Link>
        <form className="store-search" role="search" onSubmit={search}>
          <Search size={18} aria-hidden className="store-search__icon" />
          <input className="ps-input" type="search" placeholder="Ürün adı ya da marka ara" aria-label="Ürün ara" value={q} onChange={(e) => setQ(e.target.value)} />
        </form>
        <nav className="store-account" aria-label="Hesap">
          <Link to="/sepet" className="store-action store-cart" aria-label={count > 0 ? `Sepetim, ${count} ürün` : 'Sepetim'}>
            <span className="store-cart__icon"><ShoppingCart size={20} aria-hidden />{count > 0 && <span className="store-cart__badge" aria-hidden>{count > 99 ? '99+' : count}</span>}</span>
            <span>Sepetim</span>
          </Link>
          {token ? (
            <>
              <Link to="/hesap/siparisler" className="store-action"><Package size={20} aria-hidden /><span>Siparişlerim</span></Link>
              <Link to="/hesap" className="store-action" title={profile?.email}><User size={20} aria-hidden /><span className="store-user">{profile?.fullName ?? 'Hesabım'}</span></Link>
            </>
          ) : (
            <>
              <Link to="/giris" className="store-action"><LogIn size={20} aria-hidden /><span>Giriş yap</span></Link>
              <Link to="/kayit" className="ps-btn"><UserPlus size={16} aria-hidden /> Kayıt ol</Link>
            </>
          )}
        </nav>
      </header>
      {roots.length > 0 && (
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
      <main className="store-content">{children ?? <Outlet />}</main>
      <footer className="store-footer">
        <span>© {new Date().getFullYear()} {info.name}</span>
        <ThemeSwitch />
      </footer>
    </div>
  )
}
