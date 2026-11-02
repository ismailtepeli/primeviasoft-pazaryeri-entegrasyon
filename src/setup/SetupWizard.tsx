import { useEffect, useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { Button } from '../components'
import { ChannelPicker } from './ChannelPicker'
import { initialSelection, toggleChannel } from './channelSelection'
import { useSetup } from './SetupContext'
import '../pages/Settings.css'
import './Setup.css'

/** İlk girişte bir kez: hangi kanallarda satış yapılacağını seçtirir; seçim modülleri açıp kapatır. Sonra Ayarlar > Satış kanalları. */
export default function SetupWizard() {
  const { authenticated } = useAuth()
  const { setup, apply } = useSetup()
  const navigate = useNavigate()
  const [selection, setSelection] = useState<Set<string>>(new Set())
  const [ready, setReady] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  useEffect(() => {
    if (setup && !ready) { setSelection(initialSelection(setup.channels)); setReady(true) }
  }, [setup, ready])

  if (!authenticated) return <Navigate to="/giris" replace />
  if (!setup) return <div className="setup-page"><p className="setup-note" aria-busy="true">Yükleniyor…</p></div>
  if (setup.completed && !busy) return <Navigate to="/" replace />

  async function finish() {
    setBusy(true)
    setError(undefined)
    try {
      await apply([...selection])
      navigate('/', { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Kurulum tamamlanamadı.')
      setBusy(false)
    }
  }

  return (
    <div className="setup-page">
      <section className="ps-card setup-page__card" aria-labelledby="setup-title">
        <div>
          <h1 id="setup-title">Hoş geldiniz! Nerede satış yapacaksınız?</h1>
          <p className="setup-page__lead">Satış kanallarınızı seçin; yalnızca seçtiklerinizin menüleri ve işleri açılır. Sonra Ayarlar &gt; Satış kanalları'ndan istediğiniz zaman değiştirebilirsiniz.</p>
        </div>
        <ChannelPicker channels={setup.channels} selection={selection} onToggle={(id) => setSelection((s) => toggleChannel(s, id))} disabled={busy} />
        {selection.size === 0 && <p className="setup-note">Hiç kanal seçmezseniz yalnızca ürün, kategori ve stok yönetimi açık kalır.</p>}
        {error && <p className="setup-page__error" role="alert">{error}</p>}
        <div className="setup-page__actions">
          <Button variant="primary" onClick={finish} disabled={busy}>{busy ? 'Kuruluyor…' : 'Kurulumu tamamla'}</Button>
        </div>
      </section>
    </div>
  )
}
