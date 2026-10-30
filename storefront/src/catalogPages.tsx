import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useOutletContext, useParams, useSearchParams } from 'react-router-dom'
import { ChevronRight, CircleCheck, CircleOff, Folder, ImageOff, LayoutGrid, List, Minus, Package, Plus, ShoppingCart, SlidersHorizontal, TriangleAlert, X } from 'lucide-react'
import { StoreApiError, storeApi } from './api'
import {
  PAGE_SIZE, activeFilterCount, apiQuery, categoryHref, categoryTrail, checkQuantity, childrenOf, clearFilters, effectiveMin, findCategoryBySlug, formatMoney,
  parseListParams, quantityNote, sortOptions, stepQuantity, toSearchParams, vatNote,
  type ListParams, type StoreCategory, type StoreFacets, type StoreProduct, type StoreProductDetail, type StoreProductPage, type StoreStock, type ViewMode,
} from './catalog'
import { useCart } from './CartContext'
import type { CatalogContext } from './Layout'
import { useStore } from './StoreContext'

const VIEW_KEY = 'store.view'

function initialView(): ViewMode {
  try { return localStorage.getItem(VIEW_KEY) === 'table' ? 'table' : 'cards' } catch { return 'cards' }
}

function StockBadge({ stock }: { stock: StoreStock }) {
  if (stock.label === null && stock.quantity === null) return null
  const low = stock.label === 'Az kaldı'
  const text = stock.quantity !== null ? (stock.inStock ? `${stock.quantity} adet` : 'Tükendi') : stock.label
  const Icon = !stock.inStock ? CircleOff : low ? TriangleAlert : CircleCheck
  return <span className={`store-stock ${!stock.inStock ? 'store-stock--out' : low ? 'store-stock--low' : 'store-stock--ok'}`}><Icon size={14} aria-hidden /> {text}</span>
}

function Thumb({ url, name, className = '' }: { url: string | null; name: string; className?: string }) {
  return url
    ? <img className={`store-thumb ${className}`} src={url} alt={name} loading="lazy" />
    : <span className={`store-thumb store-thumb--empty ${className}`} role="img" aria-label="Görsel yok"><ImageOff size={22} aria-hidden /></span>
}

/** Adet seçici: +/- koli adedi kadar adımlar; geçersiz değerde sunucu kuralını açıklar ve tek tıkla düzeltir; geçerliyse sepete ekler. */
function QuantityStepper({ product, value, onChange, compact = false }: { product: StoreProduct; value: string; onChange: (v: string) => void; compact?: boolean }) {
  const { add } = useCart()
  const [state, setState] = useState<{ ok: boolean; text: string }>()
  const [busy, setBusy] = useState(false)
  const check = checkQuantity(value, product.minOrderQuantity, product.packSize)
  const out = !product.stock.inStock

  async function addToCart() {
    setBusy(true)
    try { await add(product.id, Number(value)); setState({ ok: true, text: 'Sepete eklendi' }) }
    catch (e) { setState({ ok: false, text: (e as Error).message }) }
    finally { setBusy(false) }
  }

  return (
    <div className={`store-qty${compact ? ' store-qty--compact' : ''}`}>
      <div className="store-qty__row">
        <div className="store-stepper" role="group" aria-label={`${product.name} adet`}>
          <button type="button" aria-label="Azalt" disabled={out} onClick={() => onChange(String(stepQuantity(value, -1, product.minOrderQuantity, product.packSize)))}><Minus size={14} aria-hidden /></button>
          <input inputMode="numeric" aria-label={`${product.name} adet`} aria-invalid={!check.valid} value={value} disabled={out} onChange={(e) => onChange(e.target.value)} />
          <button type="button" aria-label="Arttır" disabled={out} onClick={() => onChange(String(stepQuantity(value, 1, product.minOrderQuantity, product.packSize)))}><Plus size={14} aria-hidden /></button>
        </div>
        <button type="button" className="ps-btn store-add" disabled={!check.valid || out || busy} onClick={addToCart}>
          <ShoppingCart size={16} aria-hidden /> <span>{out ? 'Tükendi' : 'Sepete ekle'}</span>
        </button>
      </div>
      {!check.valid && !out && <button type="button" className="store-qty__fix" onClick={() => onChange(String(check.suggested))}>{check.error} <u>{check.suggested} yap</u></button>}
      {state && <span role={state.ok ? 'status' : 'alert'} className={state.ok ? 'store-ok' : 'store-error'}>{state.text}</span>}
    </div>
  )
}

function Pager({ page, total, onPage }: { page: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))
  if (pages <= 1) return null
  const numbers = Array.from({ length: pages }, (_, i) => i + 1).filter((n) => n === 1 || n === pages || Math.abs(n - page) <= 1)
  return (
    <nav className="store-pager" aria-label="Sayfalar">
      <button type="button" className="ps-btn" disabled={page <= 1} onClick={() => onPage(page - 1)}>Önceki</button>
      {numbers.map((n, i) => (
        <span key={n} className="store-pager__group">
          {i > 0 && n - numbers[i - 1] > 1 && <span className="store-note" aria-hidden>…</span>}
          <button type="button" className="store-pager__num" aria-current={n === page ? 'page' : undefined} onClick={() => onPage(n)}>{n}</button>
        </span>
      ))}
      <button type="button" className="ps-btn" disabled={page >= pages} onClick={() => onPage(page + 1)}>Sonraki</button>
    </nav>
  )
}

/** Sol ağaç: kök kategoriler; seçili dal açık gösterilir, sayılar alt kategorileri kapsar. */
function CategoryTree({ categories, activeId }: { categories: StoreCategory[]; activeId?: string }) {
  const trail = useMemo(() => new Set(activeId ? categoryTrail(categories, activeId).map((c) => c.id) : []), [categories, activeId])
  const render = (parent: string | null, depth: number) => {
    const items = childrenOf(categories, parent)
    if (items.length === 0) return null
    return (
      <ul className={`store-tree${depth > 0 ? ' store-tree--nested' : ''}`}>
        {items.map((c) => (
          <li key={c.id}>
            <Link to={categoryHref(c.slugPath)} className={`store-tree__link${c.id === activeId ? ' store-tree__link--active' : ''}`} aria-current={c.id === activeId ? 'page' : undefined}>
              <span>{c.name}</span><span className="store-note">{c.productCount}</span>
            </Link>
            {trail.has(c.id) && render(c.id, depth + 1)}
          </li>
        ))}
      </ul>
    )
  }
  return <nav aria-label="Kategori ağacı">{render(null, 0)}</nav>
}

interface FilterProps {
  params: ListParams
  facets: StoreFacets | null
  onChange: (next: Partial<ListParams>) => void
}

/** Marka, fiyat aralığı ve stok süzgeçleri; masaüstünde sol panel, mobilde çekmece içinde. */
function FilterPanel({ params, facets, onChange }: FilterProps) {
  const [min, setMin] = useState(params.minPrice)
  const [max, setMax] = useState(params.maxPrice)
  const [showAll, setShowAll] = useState(false)
  useEffect(() => { setMin(params.minPrice); setMax(params.maxPrice) }, [params.minPrice, params.maxPrice])
  const brands = facets?.brands ?? []
  const visibleBrands = showAll ? brands : brands.slice(0, 8)
  const toggleBrand = (name: string) => onChange({ brands: params.brands.includes(name) ? params.brands.filter((b) => b !== name) : [...params.brands, name] })
  const clean = (v: string) => v.replace(/[^\d.,]/g, '')

  return (
    <div className="store-filters">
      {brands.length > 0 && (
        <fieldset className="store-filter">
          <legend>Marka</legend>
          {visibleBrands.map((b) => (
            <label key={b.name} className="store-check"><input type="checkbox" checked={params.brands.includes(b.name)} onChange={() => toggleBrand(b.name)} /><span>{b.name}</span><span className="store-note">{b.count}</span></label>
          ))}
          {brands.length > 8 && <button type="button" className="store-link-btn" onClick={() => setShowAll((s) => !s)}>{showAll ? 'Daha az göster' : `Tümünü göster (${brands.length})`}</button>}
        </fieldset>
      )}
      <fieldset className="store-filter">
        <legend>Fiyat aralığı</legend>
        <form className="store-price-range" onSubmit={(e) => { e.preventDefault(); onChange({ minPrice: min, maxPrice: max }) }}>
          <input className="ps-input" inputMode="decimal" aria-label="En düşük fiyat" placeholder={facets?.priceMin != null ? String(Math.floor(facets.priceMin)) : 'En az'} value={min} onChange={(e) => setMin(clean(e.target.value))} />
          <span aria-hidden>–</span>
          <input className="ps-input" inputMode="decimal" aria-label="En yüksek fiyat" placeholder={facets?.priceMax != null ? String(Math.ceil(facets.priceMax)) : 'En çok'} value={max} onChange={(e) => setMax(clean(e.target.value))} />
          <button type="submit" className="ps-btn" aria-label="Fiyat aralığını uygula">Uygula</button>
        </form>
      </fieldset>
      <fieldset className="store-filter">
        <legend>Stok</legend>
        <label className="store-check"><input type="checkbox" checked={params.inStock} onChange={(e) => onChange({ inStock: e.target.checked })} /><span>Yalnızca stokta olanlar</span></label>
      </fieldset>
    </div>
  )
}

function ProductCard({ p, qty, setQty }: { p: StoreProduct; qty: string; setQty: (v: string) => void }) {
  return (
    <li className="store-card">
      <Link to={`/urun/${p.id}`} className="store-card__media" tabIndex={-1} aria-hidden><Thumb url={p.coverUrl} name={p.name} className="store-thumb--card" /></Link>
      <div className="store-card__body">
        {p.brand && <span className="store-card__brand">{p.brand}</span>}
        <Link to={`/urun/${p.id}`} className="store-card__name">{p.name}</Link>
        <span className="store-note">{p.variant ? `${p.variant} · ` : ''}{p.code}</span>
        <div className="store-card__price"><strong>{formatMoney(p.price.amount, p.price.currency)}</strong><span className="store-note">{vatNote(p.price)}</span></div>
        <div className="store-card__meta">
          <StockBadge stock={p.stock} />
          {quantityNote(p.minOrderQuantity, p.packSize) && <span className="store-chip"><Package size={13} aria-hidden /> {quantityNote(p.minOrderQuantity, p.packSize)}</span>}
        </div>
      </div>
      <QuantityStepper product={p} value={qty} onChange={setQty} compact />
    </li>
  )
}

function Skeleton({ view }: { view: ViewMode }) {
  return view === 'cards'
    ? <ul className="store-grid" aria-busy="true" aria-label="Yükleniyor">{Array.from({ length: 8 }, (_, i) => <li key={i} className="store-card store-card--skeleton"><span className="skel skel--img" /><span className="skel" /><span className="skel skel--short" /></li>)}</ul>
    : <div className="store-tablewrap" aria-busy="true" aria-label="Yükleniyor">{Array.from({ length: 6 }, (_, i) => <div key={i} className="store-table-skel"><span className="skel skel--thumb" /><span className="skel" /></div>)}</div>
}

export function ProductsPage() {
  const { token } = useStore()
  const { categories, loaded } = useOutletContext<CatalogContext>()
  const { '*': slug = '' } = useParams()
  const [search, setSearch] = useSearchParams()
  const navigate = useNavigate()
  const params = parseListParams(search)
  const category = slug ? findCategoryBySlug(categories, slug) : undefined
  const [view, setView] = useState<ViewMode>(initialView)
  const [data, setData] = useState<StoreProductPage>({ items: [], total: 0, facets: null })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()
  const [qty, setQty] = useState<Record<string, string>>({})
  const [drawer, setDrawer] = useState(false)
  const query = apiQuery(params, category?.id)
  const waiting = !!slug && !loaded
  const missing = !!slug && loaded && !category

  useEffect(() => {
    if (waiting || missing) return
    let current = true
    setLoading(true)
    storeApi<StoreProductPage>(`/catalog/products?${query}`, {}, token)
      .then((d) => { if (current) { setData(d); setError(undefined) } })
      .catch((e: Error) => { if (current) setError(e.message) })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [query, token, waiting, missing])

  useEffect(() => { if (drawer) { const prev = document.body.style.overflow; document.body.style.overflow = 'hidden'; return () => { document.body.style.overflow = prev } } }, [drawer])

  function update(next: Partial<ListParams>) { setSearch(toSearchParams({ ...params, page: 1, ...next })) }
  function changeView(v: ViewMode) {
    setView(v)
    try { localStorage.setItem(VIEW_KEY, v) } catch { /* depolama kapalı olabilir */ }
  }
  const qtyOf = (p: StoreProduct) => qty[p.id] ?? String(effectiveMin(p.minOrderQuantity, p.packSize))
  const setQtyOf = (p: StoreProduct) => (v: string) => setQty((q) => ({ ...q, [p.id]: v }))

  const trail = category ? categoryTrail(categories, category.id) : []
  const title = category ? category.name : params.q ? `"${params.q}" araması` : 'Tüm ürünler'
  const subcategories = category ? childrenOf(categories, category.id) : childrenOf(categories, null)
  const filters = activeFilterCount(params)
  const chips: { key: string; label: string; remove: () => void }[] = [
    ...params.brands.map((b) => ({ key: `b:${b}`, label: b, remove: () => update({ brands: params.brands.filter((x) => x !== b) }) })),
    ...(params.minPrice || params.maxPrice ? [{ key: 'price', label: `${params.minPrice || '0'} – ${params.maxPrice || '∞'} ₺`, remove: () => update({ minPrice: '', maxPrice: '' }) }] : []),
    ...(params.inStock ? [{ key: 'stock', label: 'Stokta olanlar', remove: () => update({ inStock: false }) }] : []),
    ...(params.q ? [{ key: 'q', label: `Arama: ${params.q}`, remove: () => update({ q: '' }) }] : []),
  ]

  if (missing) {
    return (
      <div className="store-empty"><Folder size={40} aria-hidden /><h1>Kategori bulunamadı</h1><p className="store-note">Bu kategori kaldırılmış ya da size açık değil.</p><Link to="/" className="ps-btn">Tüm ürünlere dön</Link></div>
    )
  }

  const filterPanel = <FilterPanel params={params} facets={data.facets} onChange={update} />

  return (
    <div className="store-listing">
      <aside className="store-side" aria-label="Kategoriler ve süzgeçler">
        <h2 className="store-side__title">Kategoriler</h2>
        <CategoryTree categories={categories} activeId={category?.id} />
        <h2 className="store-side__title">Süzgeçler</h2>
        {filterPanel}
      </aside>

      <div className="store-results">
        <nav className="store-crumbs" aria-label="Yol">
          <Link to="/">Tüm ürünler</Link>
          {trail.map((c, i) => <span key={c.id}><ChevronRight size={14} aria-hidden /> {i === trail.length - 1 ? <strong>{c.name}</strong> : <Link to={categoryHref(c.slugPath)}>{c.name}</Link>}</span>)}
        </nav>

        <div className="store-results__head">
          <h1>{title} <span className="store-note">{loading ? '' : `${data.total} ürün`}</span></h1>
          <div className="store-toolbar">
            <button type="button" className="ps-btn store-filter-btn" onClick={() => setDrawer(true)}><SlidersHorizontal size={16} aria-hidden /> Süzgeçler{filters > 0 ? ` (${filters})` : ''}</button>
            <label className="store-toolbar__item"><span>Sırala</span>
              <select className="ps-input" value={params.sort} onChange={(e) => update({ sort: e.target.value })}>
                {sortOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </label>
            <div className="store-view" role="group" aria-label="Görünüm">
              <button type="button" aria-label="Kart görünümü" aria-pressed={view === 'cards'} onClick={() => changeView('cards')}><LayoutGrid size={18} aria-hidden /></button>
              <button type="button" aria-label="Tablo görünümü" aria-pressed={view === 'table'} onClick={() => changeView('table')}><List size={18} aria-hidden /></button>
            </div>
          </div>
        </div>

        {subcategories.length > 0 && (
          <ul className="store-subcats" aria-label={category ? 'Alt kategoriler' : 'Ana kategoriler'}>
            {subcategories.map((c) => (
              <li key={c.id}>
                <Link to={categoryHref(c.slugPath)} className="store-subcat">
                  {c.imageUrl ? <img src={c.imageUrl} alt="" loading="lazy" /> : <span className="store-subcat__icon"><Folder size={20} aria-hidden /></span>}
                  <span className="store-subcat__name">{c.name}</span><span className="store-note">{c.productCount}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}

        {chips.length > 0 && (
          <div className="store-chips" aria-label="Seçili süzgeçler">
            {chips.map((c) => <button key={c.key} type="button" className="store-chip store-chip--removable" onClick={c.remove} aria-label={`${c.label} süzgecini kaldır`}>{c.label} <X size={13} aria-hidden /></button>)}
            <button type="button" className="store-link-btn" onClick={() => setSearch(toSearchParams({ ...clearFilters(params), q: '' }))}>Tümünü temizle</button>
          </div>
        )}

        {error && <p role="alert" className="store-error">{error}</p>}
        {loading || waiting ? <Skeleton view={view} /> : data.items.length === 0 && !error ? (
          <div className="store-empty">
            <Package size={40} aria-hidden /><h2>Aramanıza uyan ürün bulunamadı</h2>
            <p className="store-note">Süzgeçleri gevşetmeyi ya da başka bir kategoriye bakmayı deneyin.</p>
            {chips.length > 0 && <button type="button" className="ps-btn" onClick={() => setSearch(toSearchParams({ ...clearFilters(params), q: '' }))}>Süzgeçleri temizle</button>}
          </div>
        ) : view === 'cards' ? (
          <ul className="store-grid">{data.items.map((p) => <ProductCard key={p.id} p={p} qty={qtyOf(p)} setQty={setQtyOf(p)} />)}</ul>
        ) : (
          <div className="store-tablewrap">
            <table className="store-table">
              <thead><tr><th scope="col">Ürün</th><th scope="col">Fiyat</th><th scope="col">Stok</th><th scope="col">Sipariş</th></tr></thead>
              <tbody>
                {data.items.map((p) => (
                  <tr key={p.id}>
                    <td>
                      <div className="store-cell">
                        <Thumb url={p.coverUrl} name={p.name} />
                        <div>
                          {p.brand && <span className="store-card__brand">{p.brand}</span>}
                          <Link to={`/urun/${p.id}`} className="store-card__name">{p.name}</Link>
                          <div className="store-note">{p.variant ? `${p.variant} · ` : ''}{p.code}</div>
                        </div>
                      </div>
                    </td>
                    <td><strong>{formatMoney(p.price.amount, p.price.currency)}</strong><div className="store-note">{vatNote(p.price)}</div></td>
                    <td><StockBadge stock={p.stock} /></td>
                    <td><QuantityStepper product={p} value={qtyOf(p)} onChange={setQtyOf(p)} compact />{quantityNote(p.minOrderQuantity, p.packSize) && <div className="store-note">{quantityNote(p.minOrderQuantity, p.packSize)}</div>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <Pager page={params.page} total={data.total} onPage={(page) => { setSearch(toSearchParams({ ...params, page })); window.scrollTo({ top: 0 }) }} />
      </div>

      {drawer && (
        <div className="store-drawer" role="dialog" aria-modal="true" aria-label="Süzgeçler">
          <div className="store-drawer__panel">
            <div className="store-drawer__head"><strong>Kategoriler ve süzgeçler</strong><button type="button" className="store-link-btn" aria-label="Kapat" onClick={() => setDrawer(false)}><X size={20} aria-hidden /></button></div>
            <div className="store-drawer__body">
              <h2 className="store-side__title">Kategoriler</h2>
              <CategoryTree categories={categories} activeId={category?.id} />
              <h2 className="store-side__title">Süzgeçler</h2>
              {filterPanel}
            </div>
            <div className="store-drawer__foot">
              <button type="button" className="ps-btn" onClick={() => { navigate({ search: toSearchParams({ ...clearFilters(params) }).toString() }); setDrawer(false) }}>Temizle</button>
              <button type="button" className="ps-btn ps-btn--primary" onClick={() => setDrawer(false)}>{data.total} ürünü göster</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

export function ProductDetailPage() {
  const { id } = useParams()
  const { token } = useStore()
  const [p, setP] = useState<StoreProductDetail>()
  const [error, setError] = useState<string>()
  const [image, setImage] = useState(0)
  const [qty, setQty] = useState('')

  useEffect(() => {
    let current = true
    setP(undefined); setError(undefined); setImage(0)
    storeApi<StoreProductDetail>(`/catalog/products/${id}`, {}, token)
      .then((d) => { if (current) { setP(d); setQty(String(effectiveMin(d.minOrderQuantity, d.packSize))) } })
      .catch((e: unknown) => { if (current) setError(e instanceof StoreApiError && e.status === 404 ? 'Ürün bulunamadı.' : (e as Error).message) })
    return () => { current = false }
  }, [id, token])

  if (error) return <div className="store-empty"><CircleOff size={40} aria-hidden /><h1>{error}</h1><Link to="/" className="ps-btn">Ürünlere dön</Link></div>
  if (!p) return <div className="store-detail" aria-busy="true"><span className="skel skel--img" /><div><span className="skel" /><span className="skel skel--short" /></div></div>

  const asProduct: StoreProduct = { id: p.id, name: p.name, code: p.code, mainId: p.mainId, variant: p.variant, brand: p.brand, categoryPath: null, coverUrl: null,
    price: p.price, stock: p.stock, minOrderQuantity: p.minOrderQuantity, packSize: p.packSize }
  const note = quantityNote(p.minOrderQuantity, p.packSize)

  return (
    <div className="store-results">
      <nav className="store-crumbs" aria-label="Yol">
        <Link to="/">Tüm ürünler</Link>
        {p.breadcrumb.map((c) => <span key={c.id}><ChevronRight size={14} aria-hidden /> <Link to={categoryHref(c.slugPath)}>{c.name}</Link></span>)}
        <span><ChevronRight size={14} aria-hidden /> <strong>{p.name}</strong></span>
      </nav>
      <div className="store-detail">
        <div className="store-gallery">
          {p.images.length > 0 ? <img className="store-gallery__main" src={p.images[image]} alt={p.name} /> : <span className="store-gallery__main store-thumb--empty"><ImageOff size={48} aria-hidden /></span>}
          {p.images.length > 1 && (
            <div className="store-gallery__thumbs">
              {p.images.map((u, i) => (
                <button key={u} type="button" className="store-gallery__thumb" aria-label={`Görsel ${i + 1}`} aria-pressed={i === image} onClick={() => setImage(i)}><img src={u} alt="" loading="lazy" /></button>
              ))}
            </div>
          )}
        </div>
        <section className="store-detail__info">
          {p.brand && <span className="store-card__brand">{p.brand}</span>}
          <h1>{p.name}</h1>
          <p className="store-note">{p.variant ? `${p.variant} · ` : ''}Kod: {p.code}</p>
          <p className="store-price">{formatMoney(p.price.amount, p.price.currency)} <span className="store-note">{vatNote(p.price)}</span></p>
          <div className="store-card__meta">
            <StockBadge stock={p.stock} />
            {note && <span className="store-chip"><Package size={13} aria-hidden /> {note}</span>}
          </div>
          <QuantityStepper product={asProduct} value={qty} onChange={setQty} />
          {p.variants.length > 1 && (
            <div>
              <h2 className="store-h2">Varyantlar</h2>
              <ul className="store-variants">
                {p.variants.map((v) => (
                  <li key={v.id}>
                    {v.id === p.id ? <strong className="store-chip">{v.variant ?? v.code}</strong> : <Link to={`/urun/${v.id}`} className="store-chip">{v.variant ?? v.code}</Link>}
                    <span className="store-note"> {v.code} · {v.inStock ? 'stokta' : 'tükendi'}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
          {p.description && <div><h2 className="store-h2">Açıklama</h2><p className="store-desc">{p.description}</p></div>}
        </section>
      </div>
    </div>
  )
}
