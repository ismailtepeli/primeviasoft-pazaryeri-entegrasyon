import { useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { Category, Product, ProductChannel, ProductPage } from '../api/types'
import { useModules } from '../modules/ModulesContext'
import { isModuleEnabled } from '../modules/moduleState'
import { BulkBar, Button, DataTable, FilterChips, Select, StatusBadge, type Column } from '../components'
import { Link } from 'react-router-dom'
import { failureReasons, listingBadge } from './listingStatus'
import CategoryFilter from './CategoryFilter'
import CategoryManager from './CategoryManager'
import { indentedLabel } from './categoryTree'
import ProductImport from './ProductImport'
import SendToChannel from './SendToChannel'
import { RECOMMENDED_IMAGES } from './imageOrder'
import { PRODUCT_PAGE_SIZE, productsQuery, type ProductFilter } from './productFilters'
import './Products.css'

const chips: { value: ProductFilter; label: string }[] = [
  { value: 'all', label: 'Tümü' },
  { value: 'error', label: 'Hatalı' },
  { value: 'pending', label: 'Onay bekliyor' },
]

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })

type Detail = { product: Product; listing: ProductChannel }

function makeColumns(onDetail: (d: Detail) => void, withChannels: boolean, withB2B: boolean): Column<Product>[] { return [
  {
    key: 'product', header: 'Ürün',
    render: (p) => (
      <div className="product-row">
        {p.coverUrl
          ? <img className="product-thumb" src={p.coverUrl} alt="" loading="lazy" />
          : <span className="product-thumb product-thumb--empty" aria-label="Görsel yok">—</span>}
        <div className="product-cell">
          <Link to={`/urunler/${p.id}`}><strong>{p.name}</strong></Link>
          {p.variant && <span className="product-cell__barcode">{p.variant}</span>}
          <span className="product-cell__barcode">{p.barcode}{p.mainId ? ` · ${p.mainId}` : ''}</span>
          {p.imageCount < RECOMMENDED_IMAGES && <StatusBadge variant="warning">{p.imageCount}/{RECOMMENDED_IMAGES} görsel</StatusBadge>}
        </div>
      </div>
    ),
  },
  {
    key: 'category', header: 'Kategori',
    render: (p) => (p.categoryPath || p.brand)
      ? <div className="product-cell">{p.categoryPath ?? '—'}{p.brand && <span className="product-cell__barcode">{p.brand}</span>}</div>
      : '—',
  },
  { key: 'stock', header: 'Stok', render: (p) => p.stock },
  { key: 'price', header: 'Fiyat', render: (p) => money.format(p.price) },
  ...(!withB2B ? [] : [{
    key: 'b2b', header: 'B2B sitesi',
    render: (p: Product) => <StatusBadge variant={p.visibleOnB2B ? 'success' : 'inactive'}>{p.visibleOnB2B ? 'Görünür' : 'Gizli'}</StatusBadge>,
  }]),
  ...(!withChannels ? [] : [{
    key: 'channels', header: 'Kanallar',
    render: (p: Product) => p.channels.length === 0
      ? '—'
      : (
        <div className="product-channels">
          {p.channels.map((c) => (
            <button key={c.channel} type="button" className="product-channels__item"
              aria-label={`${c.channel}: ${listingBadge[c.status].text}, ayrıntıyı aç`} onClick={() => onDetail({ product: p, listing: c })}>
              {c.channel} <StatusBadge variant={listingBadge[c.status].variant}>{listingBadge[c.status].text}</StatusBadge>
            </button>
          ))}
        </div>
      ),
  }]),
] }

export default function Products() {
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<ProductFilter>('all')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<ProductPage>({ items: [], total: 0 })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()
  const [notice, setNotice] = useState<string>()
  const [detail, setDetail] = useState<Detail>()
  const [selected, setSelected] = useState<string[]>([])
  const [importing, setImporting] = useState(false)
  const [categoriesOpen, setCategoriesOpen] = useState(false)
  const [sending, setSending] = useState(false)
  const [reloadKey, setReloadKey] = useState(0)
  const [categories, setCategories] = useState<Category[]>([])
  const [category, setCategory] = useState('')
  const [moving, setMoving] = useState(false)
  const [target, setTarget] = useState('')
  const { enabled } = useModules()
  const marketplaceOn = isModuleEnabled('marketplace', enabled)
  const b2bOn = isModuleEnabled('b2b-store', enabled)
  const columns = useMemo(() => makeColumns(setDetail, marketplaceOn, b2bOn), [marketplaceOn, b2bOn])

  useEffect(() => { api<Category[]>('/categories').then(setCategories).catch(() => setCategories([])) }, [reloadKey, categoriesOpen])

  async function moveToCategory() {
    try {
      const r = await api<{ updated: number }>('/products/category', { method: 'PUT', body: JSON.stringify({ productIds: selected, categoryId: target || null }) })
      setSelected([]); setMoving(false); setTarget(''); setError(undefined); setReloadKey((k) => k + 1)
      setNotice(`${r.updated} ürünün kategorisi değiştirildi.`)
    } catch (e) { setError((e as Error).message) }
  }

  async function setB2BVisible(visible: boolean) {
    try {
      await api('/b2b/products/visibility', { method: 'PUT', body: JSON.stringify({ productIds: selected, visible }) })
      setSelected([]); setError(undefined); setReloadKey((k) => k + 1)
    } catch (e) { setError((e as Error).message) }
  }

  // Yazarken her tuşta istek atmamak için aramayı kısa bir gecikmeyle uygula.
  useEffect(() => {
    const t = setTimeout(() => { setSearch(input); setPage(1); setSelected([]) }, 300)
    return () => clearTimeout(t)
  }, [input])

  useEffect(() => {
    let current = true
    setLoading(true)
    api<ProductPage>(`/products?${productsQuery(search, filter, page, category)}`)
      .then((d) => { if (current) { setData(d); setError(undefined) } })
      .catch((e: Error) => { if (current) setError(e.message) })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false } // eski isteğin cevabı yenisini ezmesin
  }, [search, filter, page, reloadKey, category])

  // Kanal sonucu beklenen ürün varsa listeyi arada yenile; durum kendiliğinden güncellensin.
  const hasPending = data.items.some((p) => p.channels.some((c) => c.status === 'Pending'))
  useEffect(() => {
    if (!hasPending) return
    const t = setInterval(() => setReloadKey((k) => k + 1), 30000)
    return () => clearInterval(t)
  }, [hasPending])

  return (
    <div className="products-layout">
      <aside className="products-layout__side"><CategoryFilter categories={categories} value={category} onChange={(v) => { setCategory(v); setPage(1); setSelected([]) }} /></aside>
    <div className="products">
      <div className="products__toolbar">
        <input className="ps-input products__search" type="search" placeholder="Ürün adı veya barkod ara"
          aria-label="Ürün adı veya barkod ara" value={input} onChange={(e) => setInput(e.target.value)} />
        <Button onClick={() => setImporting((v) => !v)}>Excel ile yükle</Button>
        <Button onClick={() => setCategoriesOpen((v) => !v)}>Kategoriler</Button>
        {marketplaceOn && <FilterChips items={chips} value={filter} onChange={(v) => { setFilter(v as ProductFilter); setPage(1); setSelected([]) }} />}
      </div>
      {categoriesOpen && <CategoryManager onClose={() => setCategoriesOpen(false)} />}
      {importing && <ProductImport onClose={() => setImporting(false)} onImported={() => { setPage(1); setReloadKey((k) => k + 1) }} />}
      {detail && (
        <section className="ps-card listing-detail" aria-label="Kanal durumu ayrıntısı">
          <header className="listing-detail__head">
            <h2>{detail.listing.channel} · {detail.product.name}</h2>
            <StatusBadge variant={listingBadge[detail.listing.status].variant}>{listingBadge[detail.listing.status].text}</StatusBadge>
          </header>
          {failureReasons(detail.listing).length > 0
            ? <ul className="listing-detail__reasons">{failureReasons(detail.listing).map((r) => <li key={r}>{r}</li>)}</ul>
            : <p className="listing-detail__none">{listingBadge[detail.listing.status].hint}</p>}
          <div className="listing-detail__actions">
            {(detail.listing.status === 'Error' || detail.listing.status === 'Missing') && <Link to="/esleme">Eşlemeye git</Link>}
            <Button onClick={() => setDetail(undefined)}>Kapat</Button>
          </div>
        </section>
      )}
      {error && <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>}
      {notice && <p role="status" style={{ color: 'var(--success)' }}>{notice}</p>}
      <BulkBar count={selected.length} onClear={() => setSelected([])} actions={[
        { label: 'Kategori değiştir', onClick: () => { setMoving(true); setNotice(undefined) } },
        ...(marketplaceOn ? [
          { label: 'Kanala gönder', onClick: () => setSending(true) },
          { label: 'Kural uygula', disabledReason: 'Yakında' },
        ] : []),
        ...(b2bOn ? [
          { label: 'B2B sitesinde göster', onClick: () => setB2BVisible(true) },
          { label: 'B2B sitesinden kaldır', onClick: () => setB2BVisible(false) },
        ] : []),
      ]} />
      {moving && selected.length > 0 && (
        <section className="ps-card products__move" aria-label="Kategori değiştir">
          <Select label={`${selected.length} ürün için yeni kategori`} value={target} onChange={(e) => setTarget(e.target.value)}>
            <option value="">Kategorisiz yap</option>
            {categories.map((c) => <option key={c.id} value={c.id}>{indentedLabel(c)}</option>)}
          </Select>
          <div className="products__move-actions"><Button onClick={moveToCategory}>Uygula</Button><Button onClick={() => setMoving(false)}>Vazgeç</Button></div>
        </section>
      )}
      {sending && selected.length > 0 && <SendToChannel productIds={selected} onClose={() => setSending(false)} onSent={() => setReloadKey((k) => k + 1)} />}
      <DataTable columns={columns} rows={data.items} rowKey={(p) => p.id} pageSize={PRODUCT_PAGE_SIZE}
        selectable selected={selected} onSelectionChange={setSelected}
        serverPaging={{ page: page - 1, total: data.total, onPageChange: (p) => setPage(p + 1) }}
        emptyTitle={loading ? 'Yükleniyor…' : 'Ürün bulunamadı'}
        emptyText={loading ? '' : 'Aramayı, kategoriyi veya filtreyi değiştirmeyi deneyin.'} />
    </div>
    </div>
  )
}
