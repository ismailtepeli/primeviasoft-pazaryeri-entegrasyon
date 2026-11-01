import { describe, expect, it } from 'vitest'
import { badgeText, markAllRead, markRead, parseUtc, timeAgo, type NotificationPage } from './notificationView'

const now = Date.parse('2026-11-01T12:00:00Z')
const item = (id: string, read = false) => ({ id, kind: 'b2b-order-new', title: 't', body: null, link: null, createdAt: '2026-11-01T11:00:00', read })

describe('notificationView', () => {
  it('rozet 0 iken gizlenir, 99 üstünde 99+ olur', () => {
    expect([badgeText(0), badgeText(3), badgeText(99), badgeText(100)]).toEqual(['', '3', '99', '99+'])
  })

  it('saat dilimi eki olmayan sunucu zamanını UTC sayar', () => {
    expect(parseUtc('2026-11-01T11:00:00')).toBe(Date.parse('2026-11-01T11:00:00Z'))
    expect(parseUtc('2026-11-01T14:00:00+03:00')).toBe(Date.parse('2026-11-01T11:00:00Z'))
  })

  it('geçen süreyi Türkçe yazar', () => {
    expect(timeAgo('2026-11-01T11:59:40', now)).toBe('az önce')
    expect(timeAgo('2026-11-01T11:55:00', now)).toBe('5 dk önce')
    expect(timeAgo('2026-11-01T09:00:00', now)).toBe('3 sa önce')
    expect(timeAgo('2026-10-31T09:00:00', now)).toBe('dün')
    expect(timeAgo('2026-10-29T09:00:00', now)).toBe('3 gün önce')
    expect(timeAgo('2026-10-10T09:00:00', now)).toMatch(/10 Ekim/)
  })

  it('okundu işaretleme sayıyı bir kez düşürür; zaten okunmuşu ve olmayanı değiştirmez', () => {
    const page: NotificationPage = { items: [item('a'), item('b', true)], unread: 1 }
    const next = markRead(page, 'a')
    expect(next).toEqual({ items: [{ ...item('a'), read: true }, item('b', true)], unread: 0 })
    expect(markRead(next, 'a')).toBe(next)
    expect(markRead(page, 'yok')).toBe(page)
  })

  it('tümünü okundu yapar', () => {
    expect(markAllRead({ items: [item('a'), item('b')], unread: 2 })).toEqual({ items: [{ ...item('a'), read: true }, { ...item('b'), read: true }], unread: 0 })
  })
})
