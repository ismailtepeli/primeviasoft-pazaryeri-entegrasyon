import type { StockMovement, StockMovementKind } from '../api/types'
import type { BadgeVariant } from '../components'

export const movementLabel: Record<StockMovementKind, { text: string; variant: BadgeVariant }> = {
  Sale: { text: 'Satış', variant: 'info' },
  Restore: { text: 'İptal / iade', variant: 'success' },
  Adjustment: { text: 'Düzeltme', variant: 'inactive' },
}

/** "+15" / "−3" biçiminde işaretli adet. */
export function signedQuantity(q: number): string {
  return q > 0 ? `+${q}` : q < 0 ? `−${Math.abs(q)}` : '0'
}

/** Fazla satış varsa kullanıcıya gösterilecek uyarı; yoksa null. */
export function oversoldNote(m: Pick<StockMovement, 'oversold'>): string | null {
  return m.oversold > 0 ? `Stok yetmedi: ${m.oversold} adet düşülemedi (fazla satış).` : null
}
