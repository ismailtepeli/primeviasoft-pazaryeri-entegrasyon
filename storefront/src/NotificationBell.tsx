import { useCallback, useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { Bell, CheckCheck, CheckCircle2, PackageCheck, ShoppingBag, Truck, XCircle, type LucideIcon } from 'lucide-react'
import { storeApi } from './api'
import { badgeText, markAllRead, markRead, timeAgo, type NotificationPage } from './notifications'

const POLL_MS = 30_000

const icons: Record<string, LucideIcon> = {
  'order-received': ShoppingBag,
  'order-approved': CheckCircle2,
  'order-shipped': Truck,
  'order-delivered': PackageCheck,
  'order-rejected': XCircle,
  'order-cancelled': XCircle,
}

/** Bayi sitesi üst çubuğundaki bildirim zili: sipariş alındı, onaylandı, kargoya verildi gibi süreç değişikliklerini gösterir. */
export function NotificationBell({ token }: { token: string }) {
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const [unread, setUnread] = useState(0)
  const [open, setOpen] = useState(false)
  const [page, setPage] = useState<NotificationPage | null>(null)
  const [failed, setFailed] = useState(false)
  const box = useRef<HTMLDivElement>(null)

  const refreshCount = useCallback(() => {
    if (document.visibilityState === 'hidden') return
    storeApi<{ unread: number }>('/notifications/unread-count', {}, token).then((r) => setUnread(r.unread)).catch(() => { /* rozet bir sonraki turda yenilenir */ })
  }, [token])

  useEffect(() => {
    refreshCount()
    const id = window.setInterval(refreshCount, POLL_MS)
    document.addEventListener('visibilitychange', refreshCount)
    return () => { window.clearInterval(id); document.removeEventListener('visibilitychange', refreshCount) }
  }, [refreshCount])

  // Sayfa değişince (ör. sipariş verildikten sonra) rozet yoklamayı beklemeden yenilenir.
  useEffect(() => { refreshCount() }, [pathname, refreshCount])

  useEffect(() => {
    if (!open) return
    setFailed(false)
    storeApi<NotificationPage>('/notifications', {}, token).then((p) => { setPage(p); setUnread(p.unread) }).catch(() => setFailed(true))
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    const onClick = (e: MouseEvent) => { if (box.current && !box.current.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('keydown', onKey)
    document.addEventListener('mousedown', onClick)
    return () => { document.removeEventListener('keydown', onKey); document.removeEventListener('mousedown', onClick) }
  }, [open, token])

  function openItem(id: string, link: string | null) {
    const wasUnread = page?.items.find((i) => i.id === id)?.read === false
    setPage((p) => (p ? markRead(p, id) : p))
    if (wasUnread) setUnread((n) => Math.max(0, n - 1))
    storeApi(`/notifications/${id}/read`, { method: 'POST' }, token).catch(() => { /* bir sonraki yoklamada düzelir */ })
    setOpen(false)
    if (link) navigate(link)
  }

  function readAll() {
    setPage((p) => (p ? markAllRead(p) : p))
    setUnread(0)
    storeApi('/notifications/read-all', { method: 'POST' }, token).catch(() => { /* bir sonraki yoklamada düzelir */ })
  }

  const badge = badgeText(unread)
  return (
    <div className="notif" ref={box}>
      <button type="button" className="store-action store-action--btn" aria-label={badge ? `Bildirimler, ${unread} okunmamış` : 'Bildirimler'} aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        <span className="store-action__icon"><Bell size={20} aria-hidden />{badge && <span className="store-badge store-badge--alert">{badge}</span>}</span>
        <span>Bildirimler</span>
      </button>
      {open && (
        <div className="notif__panel" role="dialog" aria-label="Bildirimler">
          <div className="notif__head">
            <strong>Bildirimler</strong>
            <button type="button" className="notif__readall" onClick={readAll} disabled={!unread}><CheckCheck size={16} aria-hidden /> Tümünü okundu yap</button>
          </div>
          {failed ? <p className="notif__empty">Bildirimler yüklenemedi.</p>
            : !page ? <p className="notif__empty">Yükleniyor…</p>
            : page.items.length === 0 ? <p className="notif__empty">Henüz bildirim yok.</p>
            : (
              <ul className="notif__list">
                {page.items.map((n) => {
                  const Icon = icons[n.kind] ?? CheckCircle2
                  return (
                    <li key={n.id}>
                      <button type="button" className={`notif__item${n.read ? '' : ' notif__item--unread'}`} onClick={() => openItem(n.id, n.link)}>
                        <span className="notif__icon"><Icon size={18} aria-hidden /></span>
                        <span className="notif__text">
                          <span className="notif__title">{n.title}</span>
                          {n.body && <span className="notif__body">{n.body}</span>}
                          <span className="notif__time">{timeAgo(n.createdAt)}</span>
                        </span>
                      </button>
                    </li>
                  )
                })}
              </ul>
            )}
        </div>
      )}
    </div>
  )
}
