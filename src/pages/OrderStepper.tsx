import type { OrderRow } from '../api/types'
import { Button, StatusBadge } from '../components'
import { orderStatusBadge } from './orderView'
import { orderSteps, stoppedNote } from './orderSteps'

/** Seçili siparişin akış çubuğu: Oluştu → Toplanıyor → Faturalandı → Etiket → Kargoda. */
export default function OrderStepper({ order, onClose }: { order: OrderRow; onClose: () => void }) {
  const steps = orderSteps(order.status, order.channelStatus)
  const note = stoppedNote(order.status)
  const badge = orderStatusBadge[order.status]
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
    </section>
  )
}
