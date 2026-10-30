import { useState } from 'react'
import type { ModuleStatus } from '../api/types'
import { Button, StatusBadge } from '../components'
import { useModules } from '../modules/ModulesContext'
import { toggleBlockReason } from '../modules/moduleState'
import './Settings.css'

/** Ayarlar > Modüller: her modül için açıklama, bağımlılık uyarısı ve aç/kapa anahtarı; kapatma onay ister. */
export default function ModuleSettings() {
  const { modules, setEnabled } = useModules()
  const [confirming, setConfirming] = useState<string>()
  const [busy, setBusy] = useState<string>()
  const [error, setError] = useState<string>()

  if (!modules) return <p className="settings__muted">Yükleniyor…</p>

  async function change(m: ModuleStatus, enabled: boolean) {
    setBusy(m.id); setError(undefined)
    try { await setEnabled(m.id, enabled); setConfirming(undefined) }
    catch (e) { setError((e as Error).message) }
    finally { setBusy(undefined) }
  }

  return (
    <div className="modules">
      <p className="settings__muted">Kapalı bir modülün menüsü gizlenir ve arka plan işleri durur; verileriniz silinmez, yeniden açınca kaldığı yerden devam eder.</p>
      {error && <p className="settings__error" role="alert">{error}</p>}
      <ul className="modules__list">
        {modules.map((m) => {
          const reason = toggleBlockReason(m, modules)
          return (
            <li key={m.id} className="ps-card module">
              <div className="module__text">
                <h2>{m.name} <StatusBadge variant={m.enabled ? 'success' : 'inactive'}>{m.enabled ? 'Açık' : 'Kapalı'}</StatusBadge></h2>
                <p>{m.description}</p>
                {reason && <p className="module__reason">{reason}</p>}
              </div>
              <button type="button" role="switch" aria-checked={m.enabled} aria-label={`${m.name} modülü`}
                className="switch" disabled={!!reason || busy === m.id}
                onClick={() => (m.enabled ? setConfirming(m.id) : change(m, true))}>
                <span className="switch__thumb" />
              </button>
              {confirming === m.id && (
                <div className="module__confirm" role="alertdialog" aria-label={`${m.name} kapatılsın mı`}>
                  <p>{m.name} kapatılsın mı? Menüsü gizlenir ve işleri durur; veriniz silinmez.</p>
                  <div className="module__confirm-actions">
                    <Button onClick={() => setConfirming(undefined)}>Vazgeç</Button>
                    <Button variant="secondary" onClick={() => change(m, false)} disabled={busy === m.id}>Kapat</Button>
                  </div>
                </div>
              )}
            </li>
          )
        })}
      </ul>
    </div>
  )
}
