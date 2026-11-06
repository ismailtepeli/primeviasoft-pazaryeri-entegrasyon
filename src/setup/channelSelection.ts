import type { SalesChannel } from '../api/types'

/** Mevcut durumdan seçim: açık kanalların kimlikleri. */
export function initialSelection(channels: SalesChannel[]): Set<string> {
  return new Set(channels.filter((c) => c.enabled && c.available).map((c) => c.id))
}

export function toggleChannel(selection: Set<string>, id: string): Set<string> {
  const next = new Set(selection)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  return next
}

export function sameSelection(a: Set<string>, b: Set<string>): boolean {
  return a.size === b.size && [...a].every((x) => b.has(x))
}

/** Kaydedilince kapanacak (şu an açık, seçimden çıkarılmış) kanallar; kullanıcıya onay için gösterilir. */
export function channelsBeingClosed(channels: SalesChannel[], selection: Set<string>): SalesChannel[] {
  return channels.filter((c) => c.enabled && !selection.has(c.id))
}

export function closingNote(closing: SalesChannel[]): string {
  const names = closing.map((c) => c.name).join(', ')
  return `${names} kapatılacak: menüleri gizlenir ve arka plan işleri durur. Verileriniz silinmez, yeniden açınca kaldığı yerden devam eder.`
}

export const channelHint: Record<string, string> = {
  marketplace: 'Mağazalar, eşleme, stok-fiyat ve siparişler menüde görünür.',
  b2b: 'Bayiler, fiyat listeleri ve bayi sitesi açılır.',
  b2c: 'Ürün vitrini, sepet, ödeme, kargo ve müşteri hesapları açılır.',
}
