import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import type { OrderPage, OrderRow } from '../api/types'
import { DataTable, Select, StatusBadge, Tabs, type Column } from '../components'
import { useModules } from '../modules/ModulesContext'
import { isModuleEnabled } from '../modules/moduleState'
import B2BOrderPanel from './B2BOrderPanel'
import { b2bNextStep, b2bStatusBadge } from './b2bOrderView'
import { timeAgo } from '../lib/timeAgo'
import OrderStepper from './OrderStepper'
import { emptyCounts, initialTab, nextStep, ORDER_PAGE_SIZE, orderStatusBadge, orderTabs, ordersQuery, type OrderTab } from './orderView'
import './Orders.css'

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })

function makeColumns(onSelect: (o: OrderRow) => void): Column<OrderRow>[] { return [
  {
    key: 'order', header: 'Sipariş',
    render: (o) => (
      <div className="order-cell">
        <button type="button" className="order-cell__link" aria-label={`${o.orderNumber} sipariş akışını aç`} onClick={() => onSelect(o)}>{o.orderNumber}</button>
        <span className="order-cell__meta" title={new Date(o.orderDate).toLocaleString('tr-TR')}>{o.channel} · {timeAgo(o.orderDate)}</span>
      </div>
    ),
  },
  { key: 'customer', header: 'Müşteri', render: (o) => <div className="order-cell">{o.customerName || '—'}<span className="order-cell__meta">{o.itemCount} ürün</span></div> },
  { key: 'total', header: 'Tutar', render: (o) => money.format(o.totalPrice) },
  {
    key: 'status', header: 'Durum',
    render: (o) => {
      const b = o.channel === 'B2B' ? b2bStatusBadge[o.status] ?? orderStatusBadge[o.status] : orderStatusBadge[o.status]
      return (
        <span className="order-status">
          <StatusBadge variant={b.variant}>{b.text}</StatusBadge>
          {o.isLate && <StatusBadge variant="error">Gecikiyor</StatusBadge>}
        </span>
      )
    },
  },
  { key: 'next', header: 'Sıradaki adım', render: (o) => (o.channel === 'B2B' ? b2bNextStep(o.status) : nextStep(o.status)) },
] }

/** Tüm kanallardaki siparişler: durum sekmeleri, arama ve sunucu taraflı sayfalama. */
export default function Orders() {
  const [params] = useSearchParams()
  const [tab, setTab] = useState<OrderTab>(initialTab(params.get('tab')))
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<OrderPage>({ items: [], total: 0, counts: emptyCounts })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()
  const [selected, setSelected] = useState<OrderRow>()
  const [channel, setChannel] = useState(params.get('channel') ?? '')
  const [reloadKey, setReloadKey] = useState(0)
  const { enabled } = useModules()
  const b2bOn = isModuleEnabled('b2b-store', enabled)
  const columns = useMemo(() => makeColumns((o) => setSelected(o)), [])
  // Seçili sipariş, işlem sonrası sekme değiştirse (örn. onaylanınca Yeni'den çıksa) de panel açık kalsın.
  const selectedOrder = data.items.find((o) => o.id === selected?.id) ?? selected

  useEffect(() => {
    const t = setTimeout(() => { setSearch(input); setPage(1) }, 300)
    return () => clearTimeout(t)
  }, [input])

  useEffect(() => {
    let current = true
    setLoading(true)
    api<OrderPage>(`/orders?${ordersQuery(tab, search, page, channel)}`)
      .then((d) => { if (current) { setData(d); setError(undefined) } })
      .catch((e: Error) => { if (current) setError(e.message) })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [tab, search, page, channel, reloadKey])

  return (
    <div className="orders">
      <Tabs items={orderTabs(data.counts)} value={tab} onChange={(v) => { setTab(v as OrderTab); setPage(1); setSelected(undefined) }} />
      <div className="orders__toolbar">
        <input className="ps-input orders__search" type="search" placeholder="Sipariş no veya müşteri ara" aria-label="Sipariş no veya müşteri ara"
          value={input} onChange={(e) => setInput(e.target.value)} />
        {b2bOn && (
          <Select label="Kanal" value={channel} onChange={(e) => { setChannel(e.target.value); setPage(1); setSelected(undefined) }}>
            <option value="">Tüm kanallar</option>
            <option value="B2B">B2B bayi siparişleri</option>
            <option value="Trendyol">Trendyol</option>
          </Select>
        )}
      </div>
      {selectedOrder && (selectedOrder.channel === 'B2B'
        ? <B2BOrderPanel orderId={selectedOrder.id} onClose={() => setSelected(undefined)} onChanged={() => setReloadKey((k) => k + 1)} />
        : <OrderStepper order={selectedOrder} onClose={() => setSelected(undefined)} />)}
      {error && <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>}
      <DataTable columns={columns} rows={data.items} rowKey={(o) => o.id} pageSize={ORDER_PAGE_SIZE}
        serverPaging={{ page: page - 1, total: data.total, onPageChange: (p) => setPage(p + 1) }}
        emptyTitle={loading ? 'Yükleniyor…' : 'Bu sekmede sipariş yok'}
        emptyText={loading ? '' : 'Kanallardan sipariş geldiğinde burada görünür.'} />
    </div>
  )
}
