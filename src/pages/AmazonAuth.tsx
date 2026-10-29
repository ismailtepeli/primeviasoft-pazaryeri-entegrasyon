import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import type { AmazonOAuthStatus } from '../api/types'
import { Button } from '../components'
import { secretReminderText } from './amazonOAuth'
import './ChannelForm.css'

/** Amazon anahtarla değil, satıcının Seller Central'da yetki vermesiyle (LWA OAuth) bağlanır. */
export default function AmazonAuth({ onCancel, onChanged }: { onCancel: () => void; onChanged?: () => void }) {
  const [status, setStatus] = useState<AmazonOAuthStatus>()
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [params] = useSearchParams()
  const returned = params.get('oauth') // Amazon'dan dönüşte: "ok" | "error"

  useEffect(() => {
    api<AmazonOAuthStatus>('/channels/Amazon/oauth/status').then((s) => { setStatus(s); if (returned === 'ok') onChanged?.() }).catch((e: Error) => setError(e.message))
  }, [returned, onChanged])

  async function authorize() {
    setBusy(true)
    setError(undefined)
    try {
      const { url } = await api<{ url: string }>('/channels/Amazon/oauth/start', { method: 'POST' })
      window.location.assign(url) // Seller Central onay sayfası; işlem bitince API bizi Mağazalar'a geri yönlendirir
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Yetkilendirme başlatılamadı.')
      setBusy(false)
    }
  }

  const reminder = status && secretReminderText(status.daysUntilSecretExpiry, status.secretReminder)

  return (
    <section className="ps-card channel-form" aria-labelledby="amazon-title">
      <h2 id="amazon-title">Amazon bağlantısı</h2>
      <p className="channel-form__note">Amazon, API anahtarı yerine hesabınızla yetkilendirilerek bağlanır. Seller Central'da uygulamaya erişim izni vereceksiniz.</p>

      {returned === 'ok' && <p className="channel-form__ok" role="status">Amazon yetkilendirmesi tamamlandı.</p>}
      {returned === 'error' && <p className="channel-form__error" role="alert">{params.get('mesaj') ?? 'Amazon yetkilendirmesi tamamlanamadı.'}</p>}
      {status?.authorized && <p className="channel-form__ok">Yetki verildi{status.sellingPartnerId ? ` (satıcı: ${status.sellingPartnerId})` : ''}.</p>}
      {reminder && <p className="channel-form__error" role="alert">{reminder}</p>}
      {status && !status.configured && (
        <p className="channel-form__error" role="alert">Amazon uygulama ayarları (LWA) sunucuda tanımlı değil; yönetici ClientId, ClientSecret ve ApplicationId değerlerini girmeli.</p>
      )}
      {error && <p className="channel-form__error" role="alert">{error}</p>}

      <div className="channel-form__actions">
        <Button onClick={onCancel}>Kapat</Button>
        <Button variant="primary" onClick={authorize} disabled={busy || !status?.configured}>
          {busy ? 'Yönlendiriliyor…' : status?.authorized ? "Amazon'da yeniden yetki ver" : "Amazon'da yetki ver"}
        </Button>
      </div>
    </section>
  )
}
