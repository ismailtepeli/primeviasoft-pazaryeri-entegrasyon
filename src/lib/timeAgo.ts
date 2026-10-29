/** "az önce", "5 dk önce", "3 sa önce", "2 gün önce". */
export function timeAgo(iso: string, now: Date = new Date()): string {
  // API UTC döner ama bazı serileştirmelerde "Z" olmayabilir; UTC kabul et.
  const then = new Date(/[zZ]|[+-]\d\d:\d\d$/.test(iso) ? iso : `${iso}Z`)
  const minutes = Math.floor((now.getTime() - then.getTime()) / 60_000)
  if (minutes < 1) return 'az önce'
  if (minutes < 60) return `${minutes} dk önce`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} sa önce`
  return `${Math.floor(hours / 24)} gün önce`
}
