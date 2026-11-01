export interface NotificationItem {
  id: string
  kind: string
  title: string
  body: string | null
  link: string | null
  createdAt: string
  read: boolean
}

export interface NotificationPage { items: NotificationItem[]; unread: number }

/** Rozet metni: 0 ise boş (rozet gizlenir), 99'dan büyükse "99+". */
export function badgeText(unread: number): string {
  return unread <= 0 ? '' : unread > 99 ? '99+' : String(unread)
}

/** Sunucu UTC zamanı saat dilimi eki olmadan gönderebilir; ek yoksa UTC kabul edilir (yoksa yerel saat sanılır). */
export function parseUtc(iso: string): number {
  return Date.parse(/(Z|[+-]\d{2}:?\d{2})$/.test(iso) ? iso : iso + 'Z')
}

/** "az önce", "5 dk önce", "3 sa önce", "dün", ya da gün/ay. */
export function timeAgo(iso: string, now = Date.now()): string {
  const diff = Math.max(0, now - parseUtc(iso))
  const minutes = Math.floor(diff / 60000)
  if (minutes < 1) return 'az önce'
  if (minutes < 60) return `${minutes} dk önce`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} sa önce`
  const days = Math.floor(hours / 24)
  if (days === 1) return 'dün'
  if (days < 7) return `${days} gün önce`
  return new Date(parseUtc(iso)).toLocaleDateString('tr-TR', { day: 'numeric', month: 'long' })
}

/** Bildirimi okundu yapmanın iyimser sonucu: liste ve okunmamış sayısı birlikte güncellenir. */
export function markRead(page: NotificationPage, id: string): NotificationPage {
  const target = page.items.find((i) => i.id === id)
  if (!target || target.read) return page
  return { items: page.items.map((i) => (i.id === id ? { ...i, read: true } : i)), unread: Math.max(0, page.unread - 1) }
}

export function markAllRead(page: NotificationPage): NotificationPage {
  return { items: page.items.map((i) => ({ ...i, read: true })), unread: 0 }
}
