import { useEffect, useState } from 'react'
import { Check, PackageCheck, Truck, X } from 'lucide-react'
import { api } from '../api/client'
import type { B2BAction, B2BAdminOrder } from '../api/types'
import { Button, StatusBadge, TextInput } from '../components'
import { b2bActionLabel, b2bStatusBadge, money, primaryAction, validateReason, validateShipment } from './b2bOrderView'
import './B2BOrderPanel.css'

interface Props {
  orderId: string
  onClose: () => void
  /** Durum değişince liste yenilensin. */
  onChanged: () => void
}

const actionPath: Record<B2BAction, string> = { Approve: 'approve', Reject: 'reject', Ship: 'ship', Deliver: 'deliver', Cancel: 'cancel' }
const icons = { Approve: Check, Reject: X, Ship: Truck, Deliver: PackageCheck, Cancel: X } as const

/** B2B siparişinin ayrıntısı ve yönetici işlemleri: onay, ret (gerekçeli), sevk (kargo ve takip no), teslim, iptal. */
export default function B2BOrderPanel({ orderId, onClose, onChanged }: Props) {
  const [order, setOrder] = useState<B2BAdminOrder>()
  const [form, setForm] = useState<'reject' | 'cancel' | 'ship' | null>(null)
  const [reason, setReason] = useState('')
  const [carrier, setCarrier] = useState('')
  const [tracking, setTracking] = useState('')
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let current = true
    setOrder(undefined); setForm(null); setMessage(undefined)
    api<B2BAdminOrder>(`/b2b/orders/${orderId}`).then((o) => { if (current) setOrder(o) }).catch((e: Error) => { if (current) setMessage({ ok: false, text: e.message }) })
    return () => { current = false }
  }, [orderId])

  async function run(action: B2BAction, body: object = {}) {
    setBusy(true)
    try {
      setOrder(await api<B2BAdminOrder>(`/b2b/orders/${orderId}/${actionPath[action]}`, { method: 'POST', body: JSON.stringify(body) }))
      setForm(null); setReason(''); setMessage({ ok: true, text: `${b2bActionLabel[action]} işlemi tamamlandı.` })
      onChanged()
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
    finally { setBusy(false) }
  }

  function submitForm() {
    if (form === 'ship') {
      const error = validateShipment(carrier, tracking)
      if (error) { setMessage({ ok: false, text: error }); return }
      void run('Ship', { carrier: carrier.trim(), trackingNumber: tracking.trim() })
      return
    }
    const error = validateReason(reason, form === 'reject')
    if (error) { setMessage({ ok: false, text: error }); return }
    void run(form === 'reject' ? 'Reject' : 'Cancel', { reason: reason.trim() })
  }

  function choose(action: B2BAction) {
    setMessage(undefined)
    if (action === 'Approve' || action === 'Deliver') void run(action)
    else setForm(action === 'Ship' ? 'ship' : action === 'Reject' ? 'reject' : 'cancel')
  }

  if (!order) {
    return <section className="ps-card b2b-order" aria-busy={!message}>{message ? <p role="alert" className="b2b-order__error">{message.text}</p> : <p>Yükleniyor…</p>}<Button onClick={onClose}>Kapat</Button></section>
  }

  const badge = b2bStatusBadge[order.status] ?? { text: order.status, variant: 'info' as const }
  const actions = order.actions
  const primary = primaryAction(actions)
  return (
    <section className="ps-card b2b-order" aria-label={`${order.orderNumber} B2B sipariş ayrıntısı`}>
      <header className="b2b-order__head">
        <h2>{order.orderNumber}</h2>
        <StatusBadge variant={badge.variant}>{badge.text}</StatusBadge>
        <Button onClick={onClose}>Kapat</Button>
      </header>

      <dl className="b2b-order__meta">
        <div><dt>Bayi</dt><dd><strong>{order.dealer.name}</strong>{order.dealer.taxNumber && <span> · VN {order.dealer.taxNumber}</span>}<br />{[order.dealer.contactName, order.dealer.phone, order.dealer.email].filter(Boolean).join(' · ')}</dd></div>
        <div><dt>Teslimat adresi</dt><dd>{order.shippingAddress ?? '—'}</dd></div>
        {order.note && <div><dt>Bayi notu</dt><dd>{order.note}</dd></div>}
        {order.shipmentCarrier && <div><dt>Sevk</dt><dd>{order.shipmentCarrier} · takip no <strong>{order.trackingNumber}</strong></dd></div>}
        {order.statusNote && <div><dt>Gerekçe</dt><dd>{order.statusNote}</dd></div>}
      </dl>

      <table className="b2b-order__lines">
        <thead><tr><th scope="col">Ürün</th><th scope="col">Birim fiyat</th><th scope="col">Adet</th></tr></thead>
        <tbody>
          {order.lines.map((l) => <tr key={`${l.code}-${l.name}`}><td><strong>{l.name}</strong><br /><span className="b2b-order__muted">{l.code}</span></td><td>{money.format(l.unitPrice)} <span className="b2b-order__muted">{l.priceIncludesVat ? 'KDV dahil' : 'KDV hariç'}</span></td><td>{l.quantity}</td></tr>)}
        </tbody>
      </table>
      <p className="b2b-order__totals">KDV hariç <strong>{money.format(order.totalNet)}</strong> · Genel toplam <strong>{money.format(order.totalGross)}</strong></p>

      {message && <p className={message.ok ? 'b2b-order__ok' : 'b2b-order__error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}

      {form === 'ship' && (
        <div className="b2b-order__form">
          <TextInput label="Kargo firması" value={carrier} onChange={(e) => setCarrier(e.target.value)} />
          <TextInput label="Takip numarası" value={tracking} onChange={(e) => setTracking(e.target.value)} />
        </div>
      )}
      {(form === 'reject' || form === 'cancel') && (
        <div className="b2b-order__form">
          <TextInput label={form === 'reject' ? 'Reddetme gerekçesi (bayiye gösterilir)' : 'İptal gerekçesi (isteğe bağlı, bayiye gösterilir)'} value={reason} onChange={(e) => setReason(e.target.value)} />
        </div>
      )}

      <div className="b2b-order__actions">
        {form ? (
          <>
            <Button variant="primary" onClick={submitForm} disabled={busy}>{form === 'ship' ? 'Sevk et' : form === 'reject' ? 'Reddet' : 'İptal et'}</Button>
            <Button onClick={() => { setForm(null); setMessage(undefined) }}>Vazgeç</Button>
          </>
        ) : actions.map((a) => {
          const Icon = icons[a]
          return <Button key={a} variant={a === primary ? 'primary' : undefined} onClick={() => choose(a)} disabled={busy}><Icon size={16} aria-hidden /> {b2bActionLabel[a]}</Button>
        })}
        {!form && actions.length === 0 && <span className="b2b-order__muted">Bu sipariş için yapılacak işlem yok.</span>}
      </div>
    </section>
  )
}
