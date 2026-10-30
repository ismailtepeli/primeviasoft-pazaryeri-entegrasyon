import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { StockSyncStatus } from '../api/types'
import { StatusBadge, SyncNowButton } from '../components'
import { timeAgo } from '../lib/timeAgo'
import { pendingText, syncBadge } from './stockSync'
import './ChannelForm.css'

/** Kanalla stok-fiyat senkronunun durumu: bekleyen değişiklikler, son gönderim, hatalar ve "Şimdi senkronla". */
export default function SyncStatusCard({ channel }: { channel: string }) {
  const [status, setStatus] = useState<StockSyncStatus>()
  const [error, setError] = useState<string>()

  const load = useCallback(() => {
    api<StockSyncStatus>(`/stock/sync/${encodeURIComponent(channel)}`).then((s) => { setStatus(s); setError(undefined) }).catch((e: Error) => setError(e.message))
  }, [channel])
  useEffect(() => { setStatus(undefined); load() }, [load])

  // Gönderim ya da sonuç beklenirken durumu arada yenile.
  const active = status?.syncInFlight || (status?.pendingBatches ?? 0) > 0 || (status?.pendingChanges ?? 0) > 0
  useEffect(() => {
    if (!active) return
    const t = setInterval(load, 15000)
    return () => clearInterval(t)
  }, [active, load])

  if (error) return <p className="channel-form__error" role="alert">{error}</p>
  if (!status) return null
  const badge = syncBadge(status)

  return (
    <section className="sync-status" aria-labelledby={`sync-${channel}`}>
      <header className="sync-status__head">
        <h3 id={`sync-${channel}`}>Kanal senkronu</h3>
        <StatusBadge variant={badge.variant}>{badge.text}</StatusBadge>
      </header>
      {!status.supported ? (
        <p className="channel-form__note">{channel} için stok-fiyat senkronu henüz desteklenmiyor.</p>
      ) : (
        <>
          <p className="channel-form__note">{pendingText(status)}</p>
          <p className="channel-form__note">
            {status.lastSentAt ? `Son gönderim: ${timeAgo(status.lastSentAt)} (${status.lastSentItems} barkod).` : 'Henüz kanala stok-fiyat gönderilmedi.'}
            {status.pendingBatches > 0 && ` ${status.pendingBatches} toplu işlemin sonucu bekleniyor.`}
          </p>
          {status.lastFailed > 0 && (
            <p className="channel-form__error" role="alert">
              Son tamamlanan işlemde {status.lastFailed} barkod reddedildi ve yeniden gönderilecek: {status.lastError}
            </p>
          )}
          <div><SyncNowButton onDone={() => setTimeout(load, 500)} /></div>
        </>
      )}
    </section>
  )
}
