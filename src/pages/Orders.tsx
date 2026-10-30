import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { OrderPage, OrderRow } from '../api/types'
import { DataTable, StatusBadge, Tabs, type Column } from '../components'
import { timeAgo } from '../lib/timeAgo'
import { emptyCounts, nextStep, ORDER_PAGE_SIZE, orderStatusBadge, orderTabs, ordersQuery, type OrderTab } from './orderView'
import './Orders.css'

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })

const columns: Column<OrderRow>[] = [
  {
    key: 'order', header: 'Sipariş',
    render: (o) => (
      <div className="order-cell">
        <strong>{o.orderNumber}</strong>
        <span className="order-cell__meta" title={new Date(o.orderDate).toLocaleString('tr-TR')}>{o.channel} · {timeAgo(o.orderDate)}</span>
      </div>
    ),
  },
  { key: 'customer', header: 'Müşteri', render: (o) => <div className="order-cell">{o.customerName || '—'}<span className="order-cell__meta">{o.itemCount} ürün</span></div> },
  { key: 'total', header: 'Tutar', render: (o) => money.format(o.totalPrice) },
  {
    key: 'status', header: 'Durum',
    render: (o) => {
      const b = orderStatusBadge[o.status]
      return (
        <span className="order-status">
          <StatusBadge variant={b.variant}>{b.text}</StatusBadge>
          {o.isLate && <StatusBadge variant="error">Gecikiyor</StatusBadge>}
        </span>
      )
    },
  },
  { key: 'next', header: 'Sıradaki adım', render: (o) => nextStep(o.status) },
]

/** Tüm kanallardaki siparişler: durum sekmeleri, arama ve sunucu taraflı sayfalama. */
export default function Orders() {
  const [tab, setTab] = useState<OrderTab>('new')
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<OrderPage>({ items: [], total: 0, counts: emptyCounts })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()

  useEffect(() => {
    const t = setTimeout(() => { setSearch(input); setPage(1) }, 300)
    return () => clearTimeout(t)
  }, [input])

  useEffect(() => {
    let current = true
    setLoading(true)
    api<OrderPage>(`/orders?${ordersQuery(tab, search, page)}`)
      .then((d) => { if (current) { setData(d); setError(undefined) } })
      .catch((e: Error) => { if (current) setError(e.message) })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [tab, search, page])

  return (
    <div className="orders">
      <Tabs items={orderTabs(data.counts)} value={tab} onChange={(v) => { setTab(v as OrderTab); setPage(1) }} />
      <input className="ps-input orders__search" type="search" placeholder="Sipariş no veya müşteri ara" aria-label="Sipariş no veya müşteri ara"
        value={input} onChange={(e) => setInput(e.target.value)} />
      {error && <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>}
      <DataTable columns={columns} rows={data.items} rowKey={(o) => o.id} pageSize={ORDER_PAGE_SIZE}
        serverPaging={{ page: page - 1, total: data.total, onPageChange: (p) => setPage(p + 1) }}
        emptyTitle={loading ? 'Yükleniyor…' : 'Bu sekmede sipariş yok'}
        emptyText={loading ? '' : 'Kanallardan sipariş geldiğinde burada görünür.'} />
    </div>
  )
}
