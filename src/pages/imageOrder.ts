export const MAX_IMAGES = 8
export const RECOMMENDED_IMAGES = 3

/** Öğeyi from konumundan to konumuna taşır; sınır dışıysa listeyi değiştirmez. */
export function moveItem<T>(items: T[], from: number, to: number): T[] {
  if (from === to || from < 0 || to < 0 || from >= items.length || to >= items.length) return items
  const next = [...items]
  const [item] = next.splice(from, 1)
  next.splice(to, 0, item)
  return next
}

/** Görsel sayısına göre kullanıcıya gösterilecek durum; yeterliyse null. */
export function imageHint(count: number): string | null {
  if (count === 0) return `Hiç görsel yok. Ürün başına en az ${RECOMMENDED_IMAGES} görsel önerilir.`
  if (count < RECOMMENDED_IMAGES) return `${count} görsel var; en az ${RECOMMENDED_IMAGES} görsel önerilir.`
  return null
}
