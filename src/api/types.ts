export type ChannelState = 'Connected' | 'Unverified' | 'AuthError' | 'NotConnected'

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

export type ListingState = 'Live' | 'Missing' | 'Pending' | 'Passive' | 'Error'

export interface CatalogRun {
  kind: 'Categories' | 'Brands' | 'Attributes'
  finishedAt: string | null
  added: number
  changed: number
  removed: number
  error: string | null
}

export interface CatalogStatus {
  channel: string
  supported: boolean
  categories: number
  leafCategories: number
  brands: number
  attributeCategories: number
  syncInFlight: boolean
  lastRuns: CatalogRun[]
}

export interface ProductSendResult {
  jobs: number
  products: number
  unknown: number
}

export interface ProductImportError {
  row: number
  barcode: string
  message: string
}

export interface ProductImportResult {
  created: number
  updated: number
  groups: number
  errors: ProductImportError[]
}

export interface ProductChannel {
  channel: string
  status: ListingState
  errorMessage: string | null
}

export interface Product {
  id: string
  name: string
  barcode: string
  mainId: string | null
  variant: string | null
  brand: string | null
  /** "Giyim > Tişört" gibi tam yol; kategorisizse null. */
  categoryPath: string | null
  stock: number
  price: number
  imageCount: number
  /** Kapak görselinin adresi; görseli yoksa null. */
  coverUrl: string | null
  channels: ProductChannel[]
}

export interface ProductImage {
  id: string
  url: string
  position: number
}

export interface ProductDetail {
  id: string
  name: string
  barcode: string
  mainId: string | null
  variant: string | null
  brand: string | null
  description: string | null
  categoryId: string | null
  categoryPath: string | null
  stock: number
  price: number
  vatRate: number
  images: ProductImage[]
  channels: ProductChannel[]
}

export interface ProductInput {
  name: string
  barcode: string
  mainId: string | null
  variant: string | null
  brand: string | null
  description: string | null
  categoryId: string | null
  stock: number
  price: number
  vatRate: number
}

export interface Category {
  id: string
  name: string
  parentId: string | null
  path: string
  productCount: number
}

export interface ProductPage {
  items: Product[]
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

export interface AmazonOAuthStatus {
  /** Sunucuda LWA uygulama ayarları tanımlı mı. */
  configured: boolean
  authorized: boolean
  sellingPartnerId: string | null
  clientSecretExpiresAt: string | null
  /** Negatifse client secret'ın süresi dolmuş. */
  daysUntilSecretExpiry: number | null
  secretReminder: boolean
}

export interface LoginResponse {
  accessToken: string
  expiresAt: string
  user: { id: string; tenantId: string; email: string; fullName: string }
}
