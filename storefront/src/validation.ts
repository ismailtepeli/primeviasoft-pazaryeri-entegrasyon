export interface ApplyForm {
  companyName: string
  taxNumber: string
  taxOffice: string
  contactName: string
  email: string
  phone: string
  city: string
  address: string
  password: string
}

export const emptyApplyForm: ApplyForm = { companyName: '', taxNumber: '', taxOffice: '', contactName: '', email: '', phone: '', city: '', address: '', password: '' }

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function validateLogin(email: string, password: string): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!EMAIL.test(email.trim())) errors.email = 'Geçerli bir e-posta girin.'
  if (!password) errors.password = 'Parola zorunlu.'
  return errors
}

/** Alan adı → hata mesajı; sunucu aynı kuralları yeniden doğrular. */
export function validateApply(f: ApplyForm): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!f.companyName.trim()) errors.companyName = 'Firma adı zorunlu.'
  else if (f.companyName.trim().length > 200) errors.companyName = 'Firma adı 200 karakteri aşamaz.'
  if (!/^\d{10}$|^\d{11}$/.test(f.taxNumber.trim())) errors.taxNumber = 'Vergi numarası 10 (VKN) ya da 11 (TCKN) haneli rakam olmalı.'
  if (!EMAIL.test(f.email.trim())) errors.email = 'Geçerli bir e-posta girin.'
  if (f.password.length < 8) errors.password = 'Parola en az 8 karakter olmalı.'
  return errors
}

export const MIN_PASSWORD = 8

/** Parola sıfırlama isteği: yalnızca e-posta. */
export function validateForgot(email: string): Record<string, string> {
  return EMAIL.test(email.trim()) ? {} : { email: 'Geçerli bir e-posta girin.' }
}

/** Yeni parola + tekrarı; sıfırlama ve değiştirme ekranları ortak kullanır. */
export function validateNewPassword(password: string, confirm: string): Record<string, string> {
  const errors: Record<string, string> = {}
  if (password.length < MIN_PASSWORD) errors.password = `Parola en az ${MIN_PASSWORD} karakter olmalı.`
  else if (password.length > 100) errors.password = 'Parola en fazla 100 karakter olabilir.'
  if (confirm !== password) errors.confirm = 'Parolalar eşleşmiyor.'
  return errors
}

export function validatePasswordChange(current: string, password: string, confirm: string): Record<string, string> {
  const errors = validateNewPassword(password, confirm)
  if (!current) errors.current = 'Mevcut parola zorunlu.'
  else if (!errors.password && password === current) errors.password = 'Yeni parola mevcut parolayla aynı olamaz.'
  return errors
}
