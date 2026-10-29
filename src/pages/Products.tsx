import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { ListingState, Product, ProductPage } from '../api/types'
import { DataTable, FilterChips, StatusBadge, type BadgeVariant, type Column } from '../components'
import { PRODUCT_PAGE_SIZE, productsQuery, type ProductFilter } from './productFilters'
import './Products.css'

const chips: { value: ProductFilter; label: string }[] = [
  { value: 'all', label: 'Tümü' },
  { value: 'error', label: 'Hatalı' },
  { value: 'pending', label: 'Onay bekliyor' },
]

const listingBadge: Record<ListingState, { text: string; variant: BadgeVariant }> = {
  Live: { text: 'Yayında', variant: 'success' },
  Pending: { text: 'Onay bekliyor', variant: 'warning' },
  Error: { text: 'Hatalı', variant: 'error' },
}

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })

const columns: Column<Product>[] = [
  {
    key: 'product', header: 'Ürün',
    render: (p) => (
      <div className="product-cell">
        <strong>{p.name}</strong>
        <span className="product-cell__barcode">{p.barcode}</span>
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
            <span key={c.channel} className="product-channels__item" title={c.errorMessage ?? undefined}>
              {c.channel} <StatusBadge variant={listingBadge[c.status].variant}>{listingBadge[c.status].text}</StatusBadge>
            </span>
          ))}
        </div>
      ),
  },
]

export default function Products() {
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<ProductFilter>('all')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<ProductPage>({ items: [], total: 0 })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()

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
  }, [search, filter, page])

  return (
    <div className="products">
      <div className="products__toolbar">
        <input className="ps-input products__search" type="search" placeholder="Ürün adı veya barkod ara"
          aria-label="Ürün adı veya barkod ara" value={input} onChange={(e) => setInput(e.target.value)} />
        <FilterChips items={chips} value={filter} onChange={(v) => { setFilter(v as ProductFilter); setPage(1) }} />
      </div>
      {error && <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>}
      <DataTable columns={columns} rows={data.items} rowKey={(p) => p.id} pageSize={PRODUCT_PAGE_SIZE}
        serverPaging={{ page: page - 1, total: data.total, onPageChange: (p) => setPage(p + 1) }}
        emptyTitle={loading ? 'Yükleniyor…' : 'Ürün bulunamadı'}
        emptyText={loading ? '' : 'Aramayı veya filtreyi değiştirmeyi deneyin.'} />
    </div>
  )
}
