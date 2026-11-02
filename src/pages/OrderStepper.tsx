import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import type { OrderRow } from '../api/types'
import { Button, StatusBadge, TextInput } from '../components'
import { fulfillmentAction, invoiceNumberError, orderStatusBadge } from './orderView'
import { orderSteps, stoppedNote } from './orderSteps'

/** Seçili siparişin akış çubuğu: Oluştu → Toplanıyor → Faturalandı → Etiket → Kargoda. */
export default function OrderStepper({ order, onClose, onChanged }: { order: OrderRow; onClose: () => void; onChanged: () => void }) {
  const steps = orderSteps(order.status, order.channelStatus)
  const note = stoppedNote(order.status)
  const badge = orderStatusBadge[order.status]
  const action = fulfillmentAction(order.channel, order.channelStatus)
  const [invoice, setInvoice] = useState('')
  const [touched, setTouched] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const invoiceError = action?.kind === 'invoice' && touched ? invoiceNumberError(invoice) : undefined

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!action) return
    if (action.kind === 'invoice') {
      setTouched(true)
      if (invoiceNumberError(invoice)) return
    }
    setBusy(true)
    setError(undefined)
    try {
      await api(`/orders/${order.id}/${action.kind}`, {
        method: 'POST',
        body: action.kind === 'invoice' ? JSON.stringify({ invoiceNumber: invoice.trim() }) : undefined,
      })
      setInvoice('')
      setTouched(false)
      onChanged()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="ps-card order-detail" aria-label="Sipariş akışı">
      <header className="order-detail__head">
        <h2>{order.orderNumber} <span className="order-cell__meta">· {order.channel}</span></h2>
        <StatusBadge variant={badge.variant}>{badge.text}</StatusBadge>
        {order.isLate && <StatusBadge variant="error">Gecikiyor</StatusBadge>}
        <Button onClick={onClose}>Kapat</Button>
      </header>
      <ol className="stepper">
        {steps.map((s, i) => (
          <li key={s.label} className={`stepper__step stepper__step--${s.state}`} aria-current={s.state === 'active' ? 'step' : undefined}>
            <span className="stepper__dot" aria-hidden="true">{s.state === 'done' ? '✓' : i + 1}</span>
            <span className="stepper__label">{s.label}<span className="visually-hidden"> — {s.state === 'done' ? 'tamamlandı' : s.state === 'active' ? 'şu an' : 'bekliyor'}</span></span>
          </li>
        ))}
      </ol>
      {note && <p className="order-detail__note">{note}</p>}
      {action && (
        <form className="order-detail__action" onSubmit={submit} noValidate>
          {action.kind === 'invoice' && (
            <TextInput label="Fatura numarası" value={invoice} onChange={(e) => setInvoice(e.target.value)} onBlur={() => setTouched(true)}
              error={invoiceError} maxLength={64} autoComplete="off" />
          )}
          <Button type="submit" disabled={busy}>{busy ? 'Bildiriliyor…' : action.label}</Button>
          <p className="order-detail__note">{order.channel}'e bildirilir; kargoya verildi bilgisi kanaldan kendiliğinden gelir.</p>
        </form>
      )}
      {error && <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>}
    </section>
  )
}
