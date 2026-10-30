import type { ReactNode } from 'react'
import { Button } from './Button'

export interface BulkAction {
  label: string
  onClick?: () => void
  /** Doluysa buton pasif olur ve bu metin açıklama olarak gösterilir. */
  disabledReason?: string
}

interface BulkBarProps {
  count: number
  noun?: string
  actions: BulkAction[]
  onClear: () => void
  children?: ReactNode
}

/** Tablo satırları seçildiğinde görünen toplu işlem çubuğu. */
export function BulkBar({ count, noun = 'ürün', actions, onClear }: BulkBarProps) {
  if (count === 0) return null
  return (
    <div className="bulk-bar" role="region" aria-label="Toplu işlemler" aria-live="polite">
      <strong className="bulk-bar__count">{count} {noun} seçili</strong>
      <div className="bulk-bar__actions">
        {actions.map((a) => (
          <Button key={a.label} onClick={a.onClick} disabled={!!a.disabledReason} title={a.disabledReason}>{a.label}</Button>
        ))}
        <Button onClick={onClear}>Seçimi temizle</Button>
      </div>
    </div>
  )
}
