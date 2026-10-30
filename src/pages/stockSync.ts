import type { StockSyncStatus } from '../api/types'
import type { BadgeVariant } from '../components'

export interface SyncBadge {
  text: string
  variant: BadgeVariant
}

/** Kanal senkronunun tek bakışta durumu. */
export function syncBadge(s: Pick<StockSyncStatus, 'supported' | 'syncInFlight' | 'pendingChanges' | 'lastFailed' | 'pendingBatches'>): SyncBadge {
  if (!s.supported) return { text: 'Desteklenmiyor', variant: 'inactive' }
  if (s.syncInFlight) return { text: 'Gönderiliyor', variant: 'info' }
  if (s.lastFailed > 0) return { text: 'Hata var', variant: 'error' }
  if (s.pendingChanges > 0) return { text: 'Bekleyen değişiklik', variant: 'warning' }
  if (s.pendingBatches > 0) return { text: 'Sonuç bekleniyor', variant: 'info' }
  return { text: 'Güncel', variant: 'success' }
}

/** Bekleyen değişiklik ve ertelenenleri anlatan cümle. */
export function pendingText(s: Pick<StockSyncStatus, 'pendingChanges' | 'deferred'>): string {
  if (s.pendingChanges === 0) return 'Kanala gönderilmemiş değişiklik yok.'
  const base = `${s.pendingChanges} barkodun stok/fiyat değişikliği kanala gönderilecek (en geç 5 dakika içinde).`
  return s.deferred > 0 ? `${base} ${s.deferred} barkod dakikalık fiyat sınırı nedeniyle sonraya kaldı.` : base
}
