import type { ListingState, ProductChannel } from '../api/types'
import type { BadgeVariant } from '../components'

export const listingBadge: Record<ListingState, { text: string; variant: BadgeVariant; hint: string }> = {
  Live: { text: 'Yayında', variant: 'success', hint: 'Ürün bu kanalda yayında.' },
  Missing: { text: 'Eksik', variant: 'warning', hint: 'Bu kanal için zorunlu bilgiler eksik; ürün gönderilemedi.' },
  Pending: { text: 'Onayda', variant: 'info', hint: 'Kanal ürünü inceliyor; onaylanınca yayına girer.' },
  Passive: { text: 'Pasif', variant: 'inactive', hint: 'Ürün bu kanalda yayından kaldırılmış.' },
  Error: { text: 'Hata', variant: 'error', hint: 'Kanal ürünü reddetti.' },
}

/** Kanalın bildirdiği hata nedenleri; boş satırlar ve tekrarlar atılır. */
export function failureReasons(listing: Pick<ProductChannel, 'errorMessage'>): string[] {
  const lines = (listing.errorMessage ?? '').split(/\r?\n/).map((l) => l.trim()).filter(Boolean)
  return [...new Set(lines)]
}
