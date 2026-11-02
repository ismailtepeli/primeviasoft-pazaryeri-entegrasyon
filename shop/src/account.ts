export interface CustomerProfile {
  id: string
  email: string
  fullName: string
  phone: string | null
  hasPassword: boolean
  googleLinked: boolean
  avatarUrl: string | null
  emailVerified: boolean
  marketingConsent: boolean
  createdAt: string
}

export interface CustomerSession { accessToken: string; expiresAt: string; profile: CustomerProfile }

export interface Address {
  id: string
  title: string
  fullName: string
  phone: string
  city: string
  district: string
  addressLine: string
  postalCode: string | null
  isDefault: boolean
}

export type AddressForm = Omit<Address, 'id'>

export const emptyAddress: AddressForm = { title: '', fullName: '', phone: '', city: '', district: '', addressLine: '', postalCode: '', isDefault: false }

export const MAX_ADDRESSES = 10

export interface OrderRow { id: string; orderNumber: string; orderDate: string; status: string; total: number; currency: string; itemCount: number; firstItemName: string | null; coverUrl: string | null; paymentStatus: string | null }

export interface OrderLine { productId: string | null; name: string; quantity: number; unitPrice: number; coverUrl: string | null }

export interface OrderDetail {
  id: string
  orderNumber: string
  orderDate: string
  status: string
  total: number
  currency: string
  shippingAddress: string | null
  note: string | null
  shipmentCarrier: string | null
  trackingNumber: string | null
  shippedAt: string | null
  statusNote: string | null
  canCancel: boolean
  lines: OrderLine[]
  paymentMethod: string | null
  paymentStatus: string | null
  shippingFee: number | null
  paymentFee: number | null
  /** Ödemesi beklenen havale siparişinde müşteriye gösterilen banka bilgisi. */
  paymentInfo: string | null
  paymentExpiresAt: string | null
}

export interface OrderPage { items: OrderRow[]; total: number; pageSize: number }

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function validateEmail(email: string): string | null {
  const t = email.trim()
  return !t ? 'E-posta zorunlu.' : t.length > 254 || !EMAIL.test(t) ? 'Geçerli bir e-posta girin.' : null
}

export function validatePassword(password: string): string | null {
  return password.length < 8 ? 'Parola en az 8 karakter olmalı.' : password.length > 100 ? 'Parola en fazla 100 karakter olabilir.' : null
}

export function validateName(name: string): string | null {
  const t = name.trim()
  return t.length < 2 ? 'Ad soyad en az 2 karakter olmalı.' : t.length > 200 ? 'Ad soyad en fazla 200 karakter olabilir.' : null
}

/** Rakam, boşluk, +, -, parantez; 10-15 rakam. Zorunlu değilse boş geçilebilir. */
export function validatePhone(phone: string, required: boolean): string | null {
  const t = phone.trim()
  if (!t) return required ? 'Telefon zorunlu.' : null
  if (t.length > 30 || /[^\d\s+\-()]/.test(t)) return 'Telefon numarası geçersiz.'
  const digits = t.replace(/\D/g, '').length
  return digits < 10 || digits > 15 ? 'Telefon numarası 10 ile 15 rakam arasında olmalı.' : null
}

export interface RegisterForm { fullName: string; email: string; password: string; phone: string; acceptTerms: boolean }

export type FormErrors<T> = Partial<Record<keyof T, string>>

export function validateRegister(f: RegisterForm): FormErrors<RegisterForm> {
  const errors: FormErrors<RegisterForm> = {}
  const name = validateName(f.fullName); if (name) errors.fullName = name
  const email = validateEmail(f.email); if (email) errors.email = email
  const password = validatePassword(f.password); if (password) errors.password = password
  const phone = validatePhone(f.phone, false); if (phone) errors.phone = phone
  if (!f.acceptTerms) errors.acceptTerms = 'Devam etmek için üyelik koşullarını kabul etmelisiniz.'
  return errors
}

export function validateAddress(f: AddressForm): FormErrors<AddressForm> {
  const errors: FormErrors<AddressForm> = {}
  if (!f.title.trim() || f.title.trim().length > 60) errors.title = 'Adres başlığı zorunlu (ör. Ev, İş).'
  const name = validateName(f.fullName); if (name) errors.fullName = name
  const phone = validatePhone(f.phone, true); if (phone) errors.phone = phone
  if (!f.city.trim()) errors.city = 'İl zorunlu.'
  if (!f.district.trim()) errors.district = 'İlçe zorunlu.'
  if (f.addressLine.trim().length < 5) errors.addressLine = 'Açık adres en az 5 karakter olmalı.'
  if ((f.postalCode ?? '').trim() && !/^\d{5}$/.test((f.postalCode ?? '').trim())) errors.postalCode = 'Posta kodu 5 haneli olmalı.'
  return errors
}

export function addressSummary(a: Pick<Address, 'addressLine' | 'district' | 'city' | 'postalCode'>): string {
  return `${a.addressLine}, ${a.district} / ${a.city}${a.postalCode ? ` ${a.postalCode}` : ''}`
}

export const orderStatusLabel: Record<string, string> = {
  New: 'Sipariş alındı',
  Preparing: 'Hazırlanıyor',
  Shipped: 'Kargoya verildi',
  Delivered: 'Teslim edildi',
  Cancelled: 'İptal edildi',
  Returned: 'İade edildi',
}

export type StatusTone = 'info' | 'progress' | 'success' | 'danger'

export function orderStatusTone(status: string): StatusTone {
  return status === 'Delivered' ? 'success' : status === 'Cancelled' || status === 'Returned' ? 'danger' : status === 'New' ? 'info' : 'progress'
}

const STEPS = ['New', 'Preparing', 'Shipped', 'Delivered']

/** Sipariş ilerleme adımları: iptal/iade ilerleme göstermez (boş). Her adım: etiket ve tamamlandı/şu an durumu. */
export function orderSteps(status: string): { label: string; state: 'done' | 'current' | 'todo' }[] {
  const index = STEPS.indexOf(status)
  if (index < 0) return []
  return STEPS.map((s, i) => ({ label: orderStatusLabel[s], state: i < index || status === 'Delivered' ? 'done' : i === index ? 'current' : 'todo' }))
}

export function formatDate(iso: string): string {
  const utc = /(Z|[+-]\d{2}:?\d{2})$/.test(iso) ? iso : iso + 'Z' // sunucu saat dilimi eki olmadan UTC gönderir
  return new Date(utc).toLocaleDateString('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' })
}

/** Giriş sonrası dönülecek adres: yalnızca uygulama içi göreli yol kabul edilir (açık yönlendirme engeli). */
export function safeNext(next: string | null | undefined): string {
  return next && next.startsWith('/') && !next.startsWith('//') && !next.includes('\\') ? next : '/hesap'
}

export function pageCount(total: number, pageSize: number): number {
  return Math.max(1, Math.ceil(total / Math.max(pageSize, 1)))
}
