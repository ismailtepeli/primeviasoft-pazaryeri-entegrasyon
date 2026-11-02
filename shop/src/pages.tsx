import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { ChevronRight, CircleCheck, CircleOff, Folder, ImageOff, Package, SlidersHorizontal, TriangleAlert, X } from 'lucide-react'
import { ShopApiError } from './api'
import {
  PAGE_SIZE, activeFilterCount, apiQuery, categoryHref, categoryTrail, childrenOf, clearFilters, discountPercent, findCategoryBySlug, formatMoney,
  parseListParams, sortOptions, stockTone, toSearchParams,
  type ListParams, type ShopCategory, type ShopFacets, type ShopProduct, type ShopProductDetail, type ShopProductPage,
} from './shop'
import { metaDescription, productDescriptionFallback, productIdFromParam, productJsonLd, productPath, usePageMeta } from './seo'
import { AddToCartButton } from './cartPages'
import { useShop } from './ShopContext'

/** Mağaza açılamadığında (adres yok, mağaza kapalı, hata) tam sayfa durum ekranı. */
export function StatusPage({ title, text, action }: { title: string; text: string; action?: { label: string; onClick: () => void } }) {
  return (
    <div className="store-status">
      <div className="ps-card store-panel">
        <h1>{title}</h1>
        <p className="store-note">{text}</p>
        {action && <button type="button" className="ps-btn" onClick={action.onClick}>{action.label}</button>}
      </div>
    </div>
  )
}

function StockBadge({ inStock, label }: { inStock: boolean; label: string }) {
  const tone = stockTone(inStock, label)
  const Icon = tone === 'out' ? CircleOff : tone === 'low' ? TriangleAlert : CircleCheck
  return <span className={`store-stock store-stock--${tone}`}><Icon size={14} aria-hidden /> {label}</span>
}

function Thumb({ url, name }: { url: string | null; name: string }) {
  return url
    ? <img className="store-thumb store-thumb--card" src={url} alt={name} loading="lazy" />
    : <span className="store-thumb store-thumb--card store-thumb--empty" role="img" aria-label="Görsel yok"><ImageOff size={28} aria-hidden /></span>
}

/** Fiyat bloğu: kampanyada yeni fiyat vurgulu, normal fiyat üstü çizili ve indirim yüzdesi rozetiyle. */
function Price({ price, listPrice, currency, large = false }: { price: number; listPrice: number | null; currency: string; large?: boolean }) {
  const discount = discountPercent(price, listPrice)
  return (
    <div className={`store-priceblock${large ? ' store-priceblock--large' : ''}`}>
      <strong className={discount ? 'store-priceblock__now store-priceblock__now--sale' : 'store-priceblock__now'}>{formatMoney(price, currency)}</strong>
      {discount !== null && listPrice !== null && (
        <>
          <s className="store-priceblock__was" aria-label={`Önceki fiyat ${formatMoney(listPrice, currency)}`}>{formatMoney(listPrice, currency)}</s>
          <span className="store-sale">%{discount} indirim</span>
        </>
      )}
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
function CategoryTree({ categories, activeId }: { categories: ShopCategory[]; activeId?: string }) {
  const trail = useMemo(() => new Set(activeId ? categoryTrail(categories, activeId).map((c) => c.id) : []), [categories, activeId])
  const render = (parent: string | null, depth: number): ReactNode => {
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

/** Marka, fiyat aralığı ve stok süzgeçleri; masaüstünde sol panel, mobilde çekmece içinde. */
function FilterPanel({ params, facets, onChange }: { params: ListParams; facets: ShopFacets | null; onChange: (next: Partial<ListParams>) => void }) {
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

function ProductCard({ p }: { p: ShopProduct }) {
  const discount = discountPercent(p.price, p.listPrice)
  return (
    <li className="store-card">
      <Link to={productPath(p.id, p.name)} className="store-card__media" tabIndex={-1} aria-hidden>
        <Thumb url={p.coverUrl} name={p.name} />
        {discount !== null && <span className="store-card__ribbon">%{discount}</span>}
      </Link>
      <div className="store-card__body">
        {p.brand && <span className="store-card__brand">{p.brand}</span>}
        <Link to={productPath(p.id, p.name)} className="store-card__name">{p.name}</Link>
        <Price price={p.price} listPrice={p.listPrice} currency={p.currency} />
        <div className="store-card__meta"><StockBadge inStock={p.inStock} label={p.stockLabel} /></div>
        <div className="store-card__cta"><AddToCartButton productId={p.id} disabled={!p.inStock} /></div>
      </div>
    </li>
  )
}

function Skeleton() {
  return <ul className="store-grid" aria-busy="true" aria-label="Yükleniyor">{Array.from({ length: 8 }, (_, i) => <li key={i} className="store-card store-card--skeleton"><span className="skel skel--img" /><span className="skel" /><span className="skel skel--short" /></li>)}</ul>
}

export function ProductsPage() {
  const { api, categories, categoriesLoaded, info, tenant } = useShop()
  const { '*': slug = '' } = useParams()
  const [search, setSearch] = useSearchParams()
  const navigate = useNavigate()
  const params = parseListParams(search)
  const category = slug ? findCategoryBySlug(categories, slug) : undefined
  const [data, setData] = useState<ShopProductPage>({ items: [], total: 0, facets: { brands: [], priceMin: null, priceMax: null } })
  const [loading, setLoading] = useState(true)
  const [loadedOnce, setLoadedOnce] = useState(false) // ilk yüklemeden sonra liste yerinde kalır, yalnızca soluklaşır
  const [error, setError] = useState<string>()
  const [drawer, setDrawer] = useState(false)
  const query = apiQuery(params, category?.id)
  const waiting = !!slug && !categoriesLoaded
  const missing = !!slug && categoriesLoaded && !category

  useEffect(() => {
    if (waiting || missing) return
    let current = true
    setLoading(true)
    api<ShopProductPage>(`/products?${query}`)
      .then((d) => { if (current) { setData(d); setError(undefined); setLoadedOnce(true) } })
      .catch((e: Error) => { if (current) setError(e.message) })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [query, api, waiting, missing])

  useEffect(() => { if (drawer) { const prev = document.body.style.overflow; document.body.style.overflow = 'hidden'; return () => { document.body.style.overflow = prev } } }, [drawer])

  function update(next: Partial<ListParams>) { setSearch(toSearchParams({ ...params, page: 1, ...next })) }

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

  // Kategori/ana liste dizinlenir; arama, süzgeç ve 2. sayfa sonrası dizinlenmez ve kanonik adres süzgeçsiz sayfadır.
  const listPath = category ? categoryHref(category.slugPath) : '/'
  usePageMeta(missing ? null : {
    title: params.q ? `"${params.q}" araması | ${info.name}` : `${title} | ${info.name}`,
    description: category
      ? `${info.name} mağazasında ${category.name} kategorisindeki ${category.productCount} ürünü inceleyin; fiyatlar KDV dahildir.`
      : `${info.name} mağazasının tüm ürünleri; kategori, marka ve fiyata göre filtreleyin. Fiyatlar KDV dahildir.`,
    canonical: `${window.location.origin}${tenant.basePath}${listPath === '/' ? '' : listPath}`,
    noindex: !!params.q || filters > 0 || params.page > 1 || params.sort !== 'name',
  })

  if (missing) {
    return <div className="store-empty"><Folder size={40} aria-hidden /><h1>Kategori bulunamadı</h1><p className="store-note">Bu kategori kaldırılmış ya da mağazada yok.</p><Link to="/" className="ps-btn">Tüm ürünlere dön</Link></div>
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

      <div className={`store-results${loading && loadedOnce ? ' store-results--busy' : ''}`} aria-busy={loading}>
        {loading && loadedOnce && <div className="store-progress" role="progressbar" aria-label="Ürünler yükleniyor" />}
        <nav className="store-crumbs" aria-label="Yol">
          <Link to="/">Tüm ürünler</Link>
          {trail.map((c, i) => <span key={c.id}><ChevronRight size={14} aria-hidden /> {i === trail.length - 1 ? <strong>{c.name}</strong> : <Link to={categoryHref(c.slugPath)}>{c.name}</Link>}</span>)}
        </nav>

        <div className="store-results__head">
          <h1>{title} <span className="store-note">{loading ? 'yükleniyor…' : `${data.total} ürün`}</span></h1>
          <div className="store-toolbar">
            <button type="button" className="ps-btn store-filter-btn" onClick={() => setDrawer(true)}><SlidersHorizontal size={16} aria-hidden /> Süzgeçler{filters > 0 ? ` (${filters})` : ''}</button>
            <label className="store-toolbar__item"><span>Sırala</span>
              <select className="ps-input" value={params.sort} onChange={(e) => update({ sort: e.target.value })}>
                {sortOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </label>
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
        {waiting || (loading && !loadedOnce) ? <Skeleton /> : data.items.length === 0 && !error ? (
          <div className="store-empty">
            <Package size={40} aria-hidden /><h2>Aramanıza uyan ürün bulunamadı</h2>
            <p className="store-note">Süzgeçleri gevşetmeyi ya da başka bir kategoriye bakmayı deneyin.</p>
            {chips.length > 0 && <button type="button" className="ps-btn" onClick={() => setSearch(toSearchParams({ ...clearFilters(params), q: '' }))}>Süzgeçleri temizle</button>}
          </div>
        ) : (
          <ul className="store-grid">{data.items.map((p) => <ProductCard key={p.id} p={p} />)}</ul>
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
  const { slug } = useParams()
  const id = productIdFromParam(slug)
  const { api, info, tenant } = useShop()
  const navigate = useNavigate()
  const [p, setP] = useState<ShopProductDetail>()
  const [error, setError] = useState<string>()
  const [image, setImage] = useState(0)

  useEffect(() => {
    let current = true
    setP(undefined); setError(undefined); setImage(0)
    if (!id) { setError('Ürün bulunamadı.'); return }
    api<ShopProductDetail>(`/products/${id}`)
      .then((d) => { if (current) setP(d) })
      .catch((e: unknown) => { if (current) setError(e instanceof ShopApiError && e.status === 404 ? 'Ürün bulunamadı.' : (e as Error).message) })
    return () => { current = false }
  }, [id, api])

  // Eski ya da adı değişmiş adresler güncel okunur adrese yönlenir (geçmişe eklenmeden).
  const canonicalPath = p ? productPath(p.id, p.name) : null
  useEffect(() => {
    if (canonicalPath && slug && canonicalPath !== `/urun/${slug}`) navigate(canonicalPath, { replace: true })
  }, [canonicalPath, slug, navigate])

  const url = canonicalPath ? `${window.location.origin}${tenant.basePath}${canonicalPath}` : ''
  usePageMeta(p ? {
    title: `${p.name} | ${info.name}`,
    description: metaDescription(p.description, productDescriptionFallback(info.name, p.name, p.price, p.currency)),
    canonical: url,
    image: p.images[0] ?? null,
    type: 'product',
    jsonLd: productJsonLd({ name: p.name, description: p.description, images: p.images, brand: p.brand, price: p.price, currency: p.currency, inStock: p.inStock, url }),
  } : error ? { title: `Ürün bulunamadı | ${info.name}`, description: 'Aradığınız ürün bulunamadı.', noindex: true } : null)

  if (error) return <div className="store-empty"><CircleOff size={40} aria-hidden /><h1>{error}</h1><Link to="/" className="ps-btn">Ürünlere dön</Link></div>
  if (!p) return <div className="store-detail" aria-busy="true"><span className="skel skel--img" /><div><span className="skel" /><span className="skel skel--short" /></div></div>

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
          <Price price={p.price} listPrice={p.listPrice} currency={p.currency} large />
          <p className="store-note">KDV dahil (%{p.vatRate})</p>
          <div className="store-card__meta"><StockBadge inStock={p.inStock} label={p.stockLabel} /></div>
          <div className="store-detail__cta"><AddToCartButton productId={p.id} disabled={!p.inStock} primary /></div>
          {p.variants.length > 1 && (
            <div>
              <h2 className="store-h2">Seçenekler</h2>
              <ul className="store-variants">
                {p.variants.map((v) => (
                  <li key={v.id}>
                    {v.id === p.id ? <strong className="store-chip">{v.variant ?? 'Bu ürün'}</strong> : <Link to={productPath(v.id, p.name)} className="store-chip">{v.variant ?? 'Diğer'}</Link>}
                    {!v.inStock && <span className="store-note"> tükendi</span>}
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
