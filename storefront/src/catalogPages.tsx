import { useEffect, useState } from 'react'
import { Link, useOutletContext, useParams, useSearchParams } from 'react-router-dom'
import { StoreApiError, storeApi } from './api'
import {
  PAGE_SIZE, apiQuery, categoryTrail, checkQuantity, effectiveMin, formatMoney, parseListParams, quantityNote, sortOptions, toSearchParams, vatNote,
  type StoreCategory, type StoreProduct, type StoreProductDetail, type StoreProductPage, type StoreStock, type ViewMode,
} from './catalog'
import { useCart } from './CartContext'
import { useStore } from './StoreContext'

export interface CatalogContext { categories: StoreCategory[] }

const VIEW_KEY = 'store.view'

function initialView(): ViewMode {
  try { return localStorage.getItem(VIEW_KEY) === 'cards' ? 'cards' : 'table' } catch { return 'table' }
}

function StockBadge({ stock }: { stock: StoreStock }) {
  if (stock.label === null && stock.quantity === null) return null
  const text = stock.quantity !== null ? (stock.inStock ? `${stock.quantity} adet` : 'Tükendi') : stock.label
  return <span className={`store-stock ${stock.inStock ? (stock.label === 'Az kaldı' ? 'store-stock--low' : 'store-stock--ok') : 'store-stock--out'}`}>{text}</span>
}

function Thumb({ url, name }: { url: string | null; name: string }) {
  return url ? <img className="store-thumb" src={url} alt={name} loading="lazy" /> : <span className="store-thumb store-thumb--empty" aria-label="Görsel yok">—</span>
}

/** Adet girişi: koli/minimum kuralını yazarken gösterir ve geçerliyse ürünü sepete ekler. */
function QuantityBox({ product, value, onChange }: { product: StoreProduct; value: string; onChange: (v: string) => void }) {
  const { add } = useCart()
  const [state, setState] = useState<{ ok: boolean; text: string }>()
  const check = checkQuantity(value, product.minOrderQuantity, product.packSize)

  async function addToCart() {
    try { await add(product.id, Number(value)); setState({ ok: true, text: 'Sepete eklendi.' }) }
    catch (e) { setState({ ok: false, text: (e as Error).message }) }
  }
  return (
    <div className="store-qty">
      <div className="store-qty__row">
        <input className="ps-input store-qty__input" inputMode="numeric" aria-label={`${product.name} adet`} aria-invalid={!check.valid} value={value} onChange={(e) => onChange(e.target.value)} />
        <button type="button" className="ps-btn" disabled={!check.valid || !product.stock.inStock} onClick={addToCart}>Sepete ekle</button>
      </div>
      {state && <span role={state.ok ? 'status' : 'alert'} className={state.ok ? 'store-ok' : 'store-error'}>{state.text}</span>}
      {!check.valid && <button type="button" className="store-qty__fix" onClick={() => onChange(String(check.suggested))}>{check.error} ({check.suggested} yap)</button>}
    </div>
  )
}

function Pager({ page, total, onPage }: { page: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))
  if (pages <= 1) return null
  return (
    <nav className="store-pager" aria-label="Sayfalar">
      <button type="button" className="ps-btn" disabled={page <= 1} onClick={() => onPage(page - 1)}>Önceki</button>
      <span>Sayfa {page} / {pages}</span>
      <button type="button" className="ps-btn" disabled={page >= pages} onClick={() => onPage(page + 1)}>Sonraki</button>
    </nav>
  )
}

export function ProductsPage() {
  const { token } = useStore()
  const { categories } = useOutletContext<CatalogContext>()
  const [search, setSearch] = useSearchParams()
  const params = parseListParams(search)
  const [view, setView] = useState<ViewMode>(initialView)
  const [data, setData] = useState<StoreProductPage>({ items: [], total: 0 })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()
  const [qty, setQty] = useState<Record<string, string>>({})
  const query = apiQuery(params)

  useEffect(() => {
    let current = true
    setLoading(true)
    storeApi<StoreProductPage>(`/catalog/products?${query}`, {}, token)
      .then((d) => { if (current) { setData(d); setError(undefined) } })
      .catch((e: Error) => { if (current) setError(e.message) })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [query, token])

  function update(next: Partial<typeof params>) {
    setSearch(toSearchParams({ ...params, page: 1, ...next }))
  }
  function changeView(v: ViewMode) {
    setView(v)
    try { localStorage.setItem(VIEW_KEY, v) } catch { /* depolama kapalı olabilir */ }
  }
  const qtyOf = (p: StoreProduct) => qty[p.id] ?? String(effectiveMin(p.minOrderQuantity, p.packSize))
  const setQtyOf = (p: StoreProduct) => (v: string) => setQty((q) => ({ ...q, [p.id]: v }))

  const trail = params.category ? categoryTrail(categories, params.category) : []
  const title = trail.length ? trail[trail.length - 1].name : params.q ? `"${params.q}" araması` : 'Tüm ürünler'
  const subcategories = params.category ? categories.filter((c) => c.parentId === params.category) : []

  return (
    <div className="store-catalog">
      <nav className="store-crumbs" aria-label="Yol">
        <button type="button" className="store-link-btn" onClick={() => update({ category: '', q: '' })}>Tüm ürünler</button>
        {trail.map((c, i) => (
          <span key={c.id}> / {i === trail.length - 1 ? <strong>{c.name}</strong> : <button type="button" className="store-link-btn" onClick={() => update({ category: c.id })}>{c.name}</button>}</span>
        ))}
      </nav>
      <div className="store-catalog__head">
        <h1>{title} <span className="store-note">({data.total} ürün)</span></h1>
        <div className="store-toolbar">
          <label className="store-toolbar__item">Sırala
            <select className="ps-input" value={params.sort} onChange={(e) => update({ sort: e.target.value })}>
              {sortOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          </label>
          <div className="store-view" role="group" aria-label="Görünüm">
            <button type="button" className="ps-btn" aria-pressed={view === 'table'} onClick={() => changeView('table')}>Tablo</button>
            <button type="button" className="ps-btn" aria-pressed={view === 'cards'} onClick={() => changeView('cards')}>Kartlar</button>
          </div>
        </div>
      </div>
      {subcategories.length > 0 && (
        <div className="store-chips" aria-label="Alt kategoriler">
          {subcategories.map((c) => <button key={c.id} type="button" className="ps-btn" onClick={() => update({ category: c.id })}>{c.name} ({c.productCount})</button>)}
        </div>
      )}
      {error && <p role="alert" className="store-error">{error}</p>}
      {!loading && !error && data.items.length === 0 && <p className="store-note">Aramaya uyan ürün bulunamadı.</p>}

      {view === 'table' ? (
        <div className="store-tablewrap">
          <table className="store-table">
            <thead><tr><th>Ürün</th><th>Fiyat</th><th>Stok</th><th>Sipariş</th></tr></thead>
            <tbody>
              {data.items.map((p) => (
                <tr key={p.id}>
                  <td>
                    <div className="store-cell">
                      <Thumb url={p.coverUrl} name={p.name} />
                      <div>
                        <Link to={`/urun/${p.id}`}><strong>{p.name}</strong></Link>
                        <div className="store-note">{p.variant ? `${p.variant} · ` : ''}{p.code}{p.brand ? ` · ${p.brand}` : ''}</div>
                      </div>
                    </div>
                  </td>
                  <td><strong>{formatMoney(p.price.amount, p.price.currency)}</strong><div className="store-note">{vatNote(p.price)}</div></td>
                  <td><StockBadge stock={p.stock} /></td>
                  <td><QuantityBox product={p} value={qtyOf(p)} onChange={setQtyOf(p)} /><div className="store-note">{quantityNote(p.minOrderQuantity, p.packSize)}</div></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <ul className="store-cards">
          {data.items.map((p) => (
            <li key={p.id} className="ps-card store-card">
              <Link to={`/urun/${p.id}`} className="store-card__media"><Thumb url={p.coverUrl} name={p.name} /></Link>
              <Link to={`/urun/${p.id}`}><strong>{p.name}</strong></Link>
              <span className="store-note">{p.variant ? `${p.variant} · ` : ''}{p.code}</span>
              <div><strong>{formatMoney(p.price.amount, p.price.currency)}</strong> <span className="store-note">{vatNote(p.price)}</span></div>
              <StockBadge stock={p.stock} />
              <QuantityBox product={p} value={qtyOf(p)} onChange={setQtyOf(p)} />
              <span className="store-note">{quantityNote(p.minOrderQuantity, p.packSize)}</span>
            </li>
          ))}
        </ul>
      )}
      <Pager page={params.page} total={data.total} onPage={(page) => setSearch(toSearchParams({ ...params, page }))} />
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

  if (error) return <div className="store-catalog"><p role="alert" className="store-error">{error}</p><Link to="/">Ürünlere dön</Link></div>
  if (!p) return <p className="store-note">Yükleniyor…</p>

  const asProduct: StoreProduct = { id: p.id, name: p.name, code: p.code, mainId: p.mainId, variant: p.variant, brand: p.brand, categoryPath: null, coverUrl: null,
    price: p.price, stock: p.stock, minOrderQuantity: p.minOrderQuantity, packSize: p.packSize }

  return (
    <div className="store-catalog">
      <nav className="store-crumbs" aria-label="Yol">
        <Link to="/">Tüm ürünler</Link>
        {p.breadcrumb.map((c) => <span key={c.id}> / <Link to={`/?category=${c.id}`}>{c.name}</Link></span>)}
        <span> / <strong>{p.name}</strong></span>
      </nav>
      <div className="store-detail">
        <div className="store-gallery">
          {p.images.length > 0 ? <img className="store-gallery__main" src={p.images[image]} alt={p.name} /> : <span className="store-gallery__main store-thumb--empty">Görsel yok</span>}
          {p.images.length > 1 && (
            <div className="store-gallery__thumbs">
              {p.images.map((u, i) => (
                <button key={u} type="button" className="store-gallery__thumb" aria-label={`Görsel ${i + 1}`} aria-pressed={i === image} onClick={() => setImage(i)}><img src={u} alt="" loading="lazy" /></button>
              ))}
            </div>
          )}
        </div>
        <section className="store-detail__info">
          <h1>{p.name}</h1>
          <p className="store-note">{p.variant ? `${p.variant} · ` : ''}Kod: {p.code}{p.brand ? ` · ${p.brand}` : ''}</p>
          <p className="store-price">{formatMoney(p.price.amount, p.price.currency)} <span className="store-note">{vatNote(p.price)}</span></p>
          <StockBadge stock={p.stock} />
          <p className="store-note">{quantityNote(p.minOrderQuantity, p.packSize) || 'Adet kısıtı yok.'}</p>
          <QuantityBox product={asProduct} value={qty} onChange={setQty} />
          {p.variants.length > 1 && (
            <div>
              <h2 className="store-h2">Varyantlar</h2>
              <ul className="store-variants">
                {p.variants.map((v) => (
                  <li key={v.id}>
                    {v.id === p.id ? <strong>{v.variant ?? v.code}</strong> : <Link to={`/urun/${v.id}`}>{v.variant ?? v.code}</Link>}
                    <span className="store-note"> · {v.code} · {v.inStock ? 'stokta' : 'tükendi'}</span>
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
