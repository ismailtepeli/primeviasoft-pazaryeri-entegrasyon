import { useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { Product, ProductChannel, ProductPage } from '../api/types'
import { Button, DataTable, FilterChips, StatusBadge, type Column } from '../components'
import { Link } from 'react-router-dom'
import { failureReasons, listingBadge } from './listingStatus'
import ProductImport from './ProductImport'
import { PRODUCT_PAGE_SIZE, productsQuery, type ProductFilter } from './productFilters'
import './Products.css'

const chips: { value: ProductFilter; label: string }[] = [
  { value: 'all', label: 'Tümü' },
  { value: 'error', label: 'Hatalı' },
  { value: 'pending', label: 'Onay bekliyor' },
]

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })

type Detail = { product: Product; listing: ProductChannel }

function makeColumns(onDetail: (d: Detail) => void): Column<Product>[] { return [
  {
    key: 'product', header: 'Ürün',
    render: (p) => (
      <div className="product-cell">
        <strong>{p.name}</strong>
        {p.variant && <span className="product-cell__barcode">{p.variant}</span>}
        <span className="product-cell__barcode">{p.barcode}{p.mainId ? ` · ${p.mainId}` : ''}</span>
      </div>
    ),
  },
  { key: 'stock', header: 'Stok', render: (p) => p.stock },
  { key: 'price', header: 'Fiyat', render: (p) => money.format(p.price) },
  {
    key: 'channels', header: 'Kanallar',
    render: (p) => p.channels.length === 0
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
  },
] }

export default function Products() {
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<ProductFilter>('all')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<ProductPage>({ items: [], total: 0 })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()
  const [detail, setDetail] = useState<Detail>()
  const [importing, setImporting] = useState(false)
  const [reloadKey, setReloadKey] = useState(0)
  const columns = useMemo(() => makeColumns(setDetail), [])

  // Yazarken her tuşta istek atmamak için aramayı kısa bir gecikmeyle uygula.
  useEffect(() => {
    const t = setTimeout(() => { setSearch(input); setPage(1) }, 300)
    return () => clearTimeout(t)
  }, [input])

  useEffect(() => {
    let current = true
    setLoading(true)
    api<ProductPage>(`/products?${productsQuery(search, filter, page)}`)
      .then((d) => { if (current) { setData(d); setError(undefined) } })
      .catch((e: Error) => { if (current) setError(e.message) })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false } // eski isteğin cevabı yenisini ezmesin
  }, [search, filter, page, reloadKey])

  return (
    <div className="products">
      <div className="products__toolbar">
        <input className="ps-input products__search" type="search" placeholder="Ürün adı veya barkod ara"
          aria-label="Ürün adı veya barkod ara" value={input} onChange={(e) => setInput(e.target.value)} />
        <Button onClick={() => setImporting((v) => !v)}>Excel ile yükle</Button>
        <FilterChips items={chips} value={filter} onChange={(v) => { setFilter(v as ProductFilter); setPage(1) }} />
      </div>
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
      <DataTable columns={columns} rows={data.items} rowKey={(p) => p.id} pageSize={PRODUCT_PAGE_SIZE}
        serverPaging={{ page: page - 1, total: data.total, onPageChange: (p) => setPage(p + 1) }}
        emptyTitle={loading ? 'Yükleniyor…' : 'Ürün bulunamadı'}
        emptyText={loading ? '' : 'Aramayı veya filtreyi değiştirmeyi deneyin.'} />
    </div>
  )
}
