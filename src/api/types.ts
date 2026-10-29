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

export interface SyncNowResult {
  enqueued: number
  skipped: number
  channels: number
}

export interface CredentialField {
  name: string
  secret: boolean
  /** Açık alanların değeri; gizli alanlarda null. */
  value: string | null
  /** Gizli alanlar için "••••1234"; sunucu tam değeri asla göndermez. */
  masked: string | null
}

export interface CredentialView {
  channel: string
  environment: 'Live' | 'Staging'
  fields: CredentialField[]
}

export interface ConnectionTestResult {
  success: boolean
  message: string
  /** Kanalın HTTP kodu; ağ hatasında null. */
  statusCode: number | null
}

export interface LoginResponse {
  accessToken: string
  expiresAt: string
  user: { id: string; tenantId: string; email: string; fullName: string }
}
