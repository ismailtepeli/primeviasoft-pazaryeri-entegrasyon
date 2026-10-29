export type ChannelState = 'Connected' | 'AuthError' | 'NotConnected'

export interface ChannelStatus {
  channel: string
  status: ChannelState
  /** UTC ISO zamanı; kanalda hiç başarılı senkron yoksa null. */
  lastSyncAt: string | null
}

export interface SyncError {
  id: string
  channel: string
  type: string
  message: string
  affectedCount: number
  occurredAt: string
  /** Hatanın çözüleceği ekranın rotası, örn. "/urunler". */
  target: string
}

export interface SyncErrorPage {
  items: SyncError[]
  total: number
}

export interface LoginResponse {
  accessToken: string
  expiresAt: string
  user: { id: string; tenantId: string; email: string; fullName: string }
}
