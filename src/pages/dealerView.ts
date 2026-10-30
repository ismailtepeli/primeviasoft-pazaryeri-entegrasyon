import type { DealerCounts, DealerDetail, DealerInput, DealerStatusKind } from '../api/types'
import type { BadgeVariant, TabItem } from '../components'

export const DEALER_PAGE_SIZE = 20

export type DealerTab = 'pending' | 'active' | 'passive'

export const emptyDealerCounts: DealerCounts = { pending: 0, active: 0, passive: 0 }

/** Durum sekmeleri sayaçlarıyla; bekleyen başvurular önce gelir. */
export function dealerTabs(counts: DealerCounts): (TabItem & { value: DealerTab })[] {
  return [
    { value: 'pending', label: 'Başvurular', count: counts.pending },
    { value: 'active', label: 'Aktif', count: counts.active },
    { value: 'passive', label: 'Pasif', count: counts.passive },
  ]
}

export const dealerStatusBadge: Record<DealerStatusKind, { text: string; variant: BadgeVariant }> = {
  Pending: { text: 'Onay bekliyor', variant: 'warning' },
  Active: { text: 'Aktif', variant: 'success' },
  Passive: { text: 'Pasif', variant: 'inactive' },
}

/** Durumdan yapılabilecek geçişler (sunucu kuralıyla aynı). */
export function dealerActions(status: DealerStatusKind): { action: 'approve' | 'deactivate' | 'activate'; label: string }[] {
  switch (status) {
    case 'Pending': return [{ action: 'approve', label: 'Onayla' }, { action: 'deactivate', label: 'Reddet' }]
    case 'Active': return [{ action: 'deactivate', label: 'Pasife al' }]
    case 'Passive': return [{ action: 'activate', label: 'Aktif et' }]
  }
}

export function dealersQuery(tab: DealerTab, search: string, page: number): string {
  const qs = new URLSearchParams({ status: tab, page: String(page), pageSize: String(DEALER_PAGE_SIZE) })
  if (search.trim()) qs.set('q', search.trim())
  return qs.toString()
}

/** Form alanları metin olarak tutulur. */
export interface DealerForm {
  name: string
  taxNumber: string
  taxOffice: string
  contactName: string
  email: string
  phone: string
  city: string
  address: string
  notes: string
  groupId: string
}

export const emptyDealerForm: DealerForm = { name: '', taxNumber: '', taxOffice: '', contactName: '', email: '', phone: '', city: '', address: '', notes: '', groupId: '' }

export function formFromDealer(d: DealerDetail): DealerForm {
  return {
    name: d.name, taxNumber: d.taxNumber, taxOffice: d.taxOffice ?? '', contactName: d.contactName ?? '', email: d.email,
    phone: d.phone ?? '', city: d.city ?? '', address: d.address ?? '', notes: d.notes ?? '', groupId: d.groupId ?? '',
  }
}

/** Alan adı → hata mesajı; boşsa form geçerlidir. Sunucu aynı kuralları yeniden doğrular. */
export function validateDealerForm(f: DealerForm): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!f.name.trim()) errors.name = 'Firma adı boş olamaz.'
  else if (f.name.trim().length > 200) errors.name = 'Firma adı 200 karakteri aşamaz.'
  if (!/^\d{10}$|^\d{11}$/.test(f.taxNumber.trim())) errors.taxNumber = 'Vergi numarası 10 (VKN) ya da 11 (TCKN) haneli rakam olmalı.'
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(f.email.trim())) errors.email = 'Geçerli bir e-posta girin.'
  return errors
}

const orNull = (s: string) => (s.trim() === '' ? null : s.trim())

export function toDealerInput(f: DealerForm): DealerInput {
  return {
    name: f.name.trim(), taxNumber: f.taxNumber.trim(), taxOffice: orNull(f.taxOffice), contactName: orNull(f.contactName), email: f.email.trim(),
    phone: orNull(f.phone), address: orNull(f.address), city: orNull(f.city), notes: orNull(f.notes), groupId: f.groupId || null,
  }
}

/** Kullanıcı ekleme formu için doğrulama. */
export function validateUserForm(f: { email: string; fullName: string; password: string }): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!f.fullName.trim()) errors.fullName = 'Ad soyad boş olamaz.'
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(f.email.trim())) errors.email = 'Geçerli bir e-posta girin.'
  if (f.password.length < 8) errors.password = 'Parola en az 8 karakter olmalı.'
  return errors
}
