import { useEffect, useRef, useState } from 'react'

interface GoogleId {
  initialize: (config: { client_id: string; callback: (response: { credential?: string }) => void; ux_mode?: string }) => void
  renderButton: (el: HTMLElement, options: Record<string, unknown>) => void
}

declare global {
  interface Window { google?: { accounts: { id: GoogleId } } }
}

const SCRIPT_SRC = 'https://accounts.google.com/gsi/client'
let scriptPromise: Promise<void> | null = null

/** Google Identity Services betiğini bir kez yükler. */
function loadScript(): Promise<void> {
  if (window.google?.accounts?.id) return Promise.resolve()
  scriptPromise ??= new Promise<void>((resolve, reject) => {
    const el = document.createElement('script')
    el.src = SCRIPT_SRC
    el.async = true
    el.onload = () => resolve()
    el.onerror = () => { scriptPromise = null; reject(new Error('Google betiği yüklenemedi')) }
    document.head.appendChild(el)
  })
  return scriptPromise
}

/**
 * "Google ile devam et" düğmesi (Google Identity Services). Kullanıcı hesabını seçince Google'ın verdiği kimlik belirteci
 * (credential) <paramref name="onCredential"/> ile döner; sunucu belirteci doğrular. Betik yüklenemezse (reklam engelleyici, çevrimdışı) açıklama gösterilir.
 */
export function GoogleButton({ clientId, onCredential }: { clientId: string; onCredential: (idToken: string) => void }) {
  const box = useRef<HTMLDivElement>(null)
  const callback = useRef(onCredential)
  callback.current = onCredential
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let active = true
    loadScript().then(() => {
      if (!active || !box.current || !window.google) return
      window.google.accounts.id.initialize({ client_id: clientId, callback: (r) => { if (r.credential) callback.current(r.credential) } })
      window.google.accounts.id.renderButton(box.current, { type: 'standard', theme: 'outline', size: 'large', shape: 'pill', text: 'continue_with', locale: 'tr', width: 320 })
    }).catch(() => { if (active) setFailed(true) })
    return () => { active = false }
  }, [clientId])

  return failed
    ? <p className="store-note" role="status">Google ile giriş şu an yüklenemedi (reklam engelleyici ya da bağlantı sorunu olabilir). E-posta ile devam edebilirsiniz.</p>
    : <div ref={box} className="google-btn" aria-label="Google ile devam et" />
}
