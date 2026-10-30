import type { ProductInput } from '../api/types'

/** Form alanları metin olarak tutulur; sayılar kaydederken dönüştürülür. */
export interface ProductForm {
  name: string
  barcode: string
  mainId: string
  variant: string
  brand: string
  description: string
  categoryId: string
  stock: string
  price: string
  vatRate: string
}

export const emptyForm: ProductForm = {
  name: '', barcode: '', mainId: '', variant: '', brand: '', description: '', categoryId: '', stock: '0', price: '', vatRate: '20',
}

export const VAT_RATES = [0, 1, 10, 20]

const orNull = (s: string) => (s.trim() === '' ? null : s.trim())

/** Alan adı → hata mesajı; boşsa form geçerlidir. Sunucu aynı kuralları yeniden doğrular. */
export function validateForm(f: ProductForm): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!f.name.trim()) errors.name = 'Ürün adı boş olamaz.'
  else if (f.name.trim().length > 500) errors.name = 'Ürün adı 500 karakteri aşamaz.'
  if (!f.barcode.trim()) errors.barcode = 'Barkod boş olamaz.'
  else if (f.barcode.trim().length > 100) errors.barcode = 'Barkod 100 karakteri aşamaz.'
  if (f.mainId.trim().length > 100) errors.mainId = 'Ana kod 100 karakteri aşamaz.'
  if (f.variant.trim().length > 500) errors.variant = 'Varyant 500 karakteri aşamaz.'
  if (f.brand.trim().length > 200) errors.brand = 'Marka 200 karakteri aşamaz.'
  if (f.description.length > 5000) errors.description = 'Açıklama 5000 karakteri aşamaz.'

  const stock = Number(f.stock)
  if (f.stock.trim() === '' || !Number.isInteger(stock) || stock < 0) errors.stock = 'Stok sıfır ya da pozitif tam sayı olmalı.'
  const price = Number(f.price.replace(',', '.'))
  if (f.price.trim() === '' || !Number.isFinite(price) || price <= 0) errors.price = 'Fiyat sıfırdan büyük bir sayı olmalı.'
  if (!VAT_RATES.includes(Number(f.vatRate))) errors.vatRate = 'KDV oranı 0, 1, 10 ya da 20 olmalı.'
  return errors
}

export function toInput(f: ProductForm): ProductInput {
  return {
    name: f.name.trim(),
    barcode: f.barcode.trim(),
    mainId: orNull(f.mainId),
    variant: orNull(f.variant),
    brand: orNull(f.brand),
    description: orNull(f.description),
    categoryId: f.categoryId || null,
    stock: Number(f.stock),
    price: Number(f.price.replace(',', '.')),
    vatRate: Number(f.vatRate),
  }
}
