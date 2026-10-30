import { Link } from 'react-router-dom'

interface KpiCardProps {
  label: string
  value: string | number
  hint?: string
  /** Verilirse kart tıklanabilir bir bağlantı olur. */
  to?: string
  /** 'danger' değeri kırmızı gösterir (örn. senkron hatası). */
  tone?: 'danger'
}

export function KpiCard({ label, value, hint, to, tone }: KpiCardProps) {
  const body = (
    <>
      <span className="kpi__label">{label}</span>
      <span className="kpi__value">{value}</span>
      {hint && <span className="kpi__hint">{hint}</span>}
    </>
  )
  const className = `ps-card kpi${tone ? ` kpi--${tone}` : ''}${to ? ' kpi--link' : ''}`
  return to ? <Link to={to} className={className}>{body}</Link> : <div className={className}>{body}</div>
}
