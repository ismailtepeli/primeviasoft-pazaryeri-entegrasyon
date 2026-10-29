import { useState } from 'react'
import { api } from '../api/client'
import type { SyncNowResult } from '../api/types'
import { Button } from './Button'

type Outcome = { kind: 'success' | 'error'; text: string }

export function describe(r: SyncNowResult): string {
  if (r.channels === 0) return 'Bağlı kanal yok; önce Mağazalar ekranından bir kanal bağlayın.'
  if (r.enqueued === 0 && r.skipped > 0) return 'Senkron zaten sırada, yeni iş eklenmedi.'
  if (r.enqueued === 0) return 'Kuyruğa alınacak senkron işi yok.'
  return `${r.enqueued} senkron işi kuyruğa alındı.`
}

/** "Şimdi senkronla": bağlı kanalların işlerini kuyruğa alır, sonucu butonun yanında bildirir. */
export function SyncNowButton({ onDone }: { onDone?: () => void }) {
  const [busy, setBusy] = useState(false)
  const [outcome, setOutcome] = useState<Outcome>()

  async function run() {
    setBusy(true)
    setOutcome(undefined)
    try {
      const result = await api<SyncNowResult>('/sync/now', { method: 'POST' })
      setOutcome({ kind: 'success', text: describe(result) })
      onDone?.()
    } catch (e) {
      setOutcome({ kind: 'error', text: e instanceof Error ? e.message : 'Senkron başlatılamadı.' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="sync-now">
      <span className={`sync-now__result sync-now__result--${outcome?.kind ?? 'none'}`} role="status" aria-live="polite">
        {outcome?.text}
      </span>
      <Button variant="primary" onClick={run} disabled={busy} aria-busy={busy}>
        {busy ? 'Senkronlanıyor…' : 'Şimdi senkronla'}
      </Button>
    </div>
  )
}
