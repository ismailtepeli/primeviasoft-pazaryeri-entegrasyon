import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { StockMovement, StockMovementPage } from '../api/types'
import { DataTable, StatusBadge, type Column } from '../components'
import { timeAgo } from '../lib/timeAgo'
import { movementLabel, oversoldNote, signedQuantity } from './stockMovements'
import './ChannelForm.css'

const PAGE_SIZE = 15

const columns: Column<StockMovement>[] = [
  { key: 'at', header: 'Zaman', render: (m) => <span title={new Date(m.at).toLocaleString('tr-TR')}>{timeAgo(m.at)}</span> },
  { key: 'product', header: 'Ürün', render: (m) => <div className="stock-product"><strong>{m.name}</strong><span>{m.barcode}</span></div> },
  { key: 'type', header: 'Hareket', render: (m) => { const l = movementLabel[m.type]; return <StatusBadge variant={l.variant}>{l.text}</StatusBadge> } },
  { key: 'channel', header: 'Kanal', render: (m) => m.channel },
  {
    key: 'qty', header: 'Adet',
    render: (m) => (
      <span className={m.oversold > 0 ? 'stock__bad' : undefined} title={oversoldNote(m) ?? undefined}>
        <strong>{signedQuantity(m.quantity)}</strong>{m.oversold > 0 && ` (+${m.oversold} yetmedi)`}
      </span>
    ),
  },
  { key: 'after', header: 'Stok', render: (m) => m.stockAfter },
  { key: 'ref', header: 'Referans', render: (m) => <span className="stock-product"><span>{m.reference.length > 24 ? m.reference.slice(0, 22) + '…' : m.reference}</span></span> },
]

/** Stok defteri: satışlar, iptal/iade geri eklemeleri ve elle düzeltmeler. */
export default function StockMovementsSection() {
  const [search, setSearch] = useState('')
  const [term, setTerm] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<StockMovementPage>({ items: [], total: 0 })
  const [error, setError] = useState<string>()

  useEffect(() => {
    const t = setTimeout(() => { setTerm(search); setPage(1) }, 300)
    return () => clearTimeout(t)
  }, [search])

  useEffect(() => {
    let current = true
    const qs = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (term.trim()) qs.set('q', term.trim())
    api<StockMovementPage>(`/stock/movements?${qs}`)
      .then((d) => { if (current) { setData(d); setError(undefined) } })
      .catch((e: Error) => { if (current) setError(e.message) })
    return () => { current = false }
  }, [term, page])

  return (
    <section className="ps-card stock" aria-labelledby="movements-title">
      <h2 id="movements-title">Stok hareketleri</h2>
      <p className="pricing__formula">Siparişle düşen, iptal/iadeyle geri eklenen ve elle düzeltilen tüm stok değişiklikleri. Aynı sipariş yeniden işlense de stok bir kez değişir.</p>
      <input className="ps-input stock__search" type="search" placeholder="Ürün, barkod ya da sipariş ara" aria-label="Hareketlerde ara" value={search} onChange={(e) => setSearch(e.target.value)} />
      {error && <p className="channel-form__error" role="alert">{error}</p>}
      <DataTable columns={columns} rows={data.items} rowKey={(m) => m.id} pageSize={PAGE_SIZE}
        serverPaging={{ page: page - 1, total: data.total, onPageChange: (p) => setPage(p + 1) }}
        emptyTitle="Henüz stok hareketi yok" emptyText="Sipariş geldiğinde ya da ürün stoğunu elle değiştirdiğinizde burada görünür." />
    </section>
  )
}
