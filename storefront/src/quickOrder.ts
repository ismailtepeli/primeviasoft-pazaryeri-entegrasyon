import type { Cart } from './orders'

export interface QuickOrderRowError { row: number; code: string; message: string }

export interface QuickOrderResult { added: number; errors: QuickOrderRowError[]; cart: Cart }

export const MAX_UPLOAD_BYTES = 2_000_000
const EXTENSIONS = ['.xlsx', '.csv', '.txt']

/** Yüklenecek dosyanın biçimi ve boyutu uygunsa null, değilse kullanıcıya gösterilecek hata. */
export function validateUpload(file: { name: string; size: number }): string | null {
  const name = file.name.toLowerCase()
  if (!EXTENSIONS.some((e) => name.endsWith(e))) return 'Yalnızca .xlsx, .csv ya da .txt dosyası yükleyin.'
  if (file.size === 0) return 'Dosya boş.'
  if (file.size > MAX_UPLOAD_BYTES) return 'Dosya 2 MB sınırını aşıyor.'
  return null
}

/** Yapıştırılan metinde dolu satır sayısı (boş satırlar sayılmaz). */
export function countLines(text: string): number {
  return text.split(/\r?\n/).filter((l) => l.trim().length > 0).length
}

/** Sonuç özeti: kaç ürün eklendi, kaç satır sorunlu. */
export function summarize(r: Pick<QuickOrderResult, 'added' | 'errors'>): string {
  const parts: string[] = []
  parts.push(r.added > 0 ? `${r.added} ürün sepete eklendi.` : 'Sepete ürün eklenmedi.')
  if (r.errors.length > 0) parts.push(`${r.errors.length} satırda sorun var.`)
  return parts.join(' ')
}

/** Hata listesini panoya kopyalanabilir metne çevirir ("Satır 3 · KOD: mesaj"). */
export function errorsAsText(errors: QuickOrderRowError[]): string {
  return errors.map((e) => `Satır ${e.row}${e.code ? ` · ${e.code}` : ''}: ${e.message}`).join('\n')
}
