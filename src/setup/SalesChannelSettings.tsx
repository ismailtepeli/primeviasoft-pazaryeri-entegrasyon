import { useEffect, useState } from 'react'
import { Button } from '../components'
import { ChannelPicker } from './ChannelPicker'
import { channelsBeingClosed, closingNote, initialSelection, sameSelection, toggleChannel } from './channelSelection'
import { useSetup } from './SetupContext'
import '../pages/Settings.css'
import './Setup.css'

/** Ayarlar > Satış kanalları: kanalları aç/kapa; kapatılacak kanal varsa kaydetmeden önce onay ister. */
export default function SalesChannelSettings() {
  const { setup, apply } = useSetup()
  const [selection, setSelection] = useState<Set<string>>(new Set())
  const [loaded, setLoaded] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()

  useEffect(() => {
    if (setup) { setSelection(initialSelection(setup.channels)); setLoaded(true) }
  }, [setup])

  if (!setup || !loaded) return <p className="settings__muted">Yükleniyor…</p>

  const saved = initialSelection(setup.channels)
  const dirty = !sameSelection(selection, saved)
  const closing = channelsBeingClosed(setup.channels, selection)

  async function save() {
    setBusy(true)
    setMessage(undefined)
    try {
      await apply([...selection])
      setConfirming(false)
      setMessage({ ok: true, text: 'Satış kanalları güncellendi.' })
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Kaydedilemedi.' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="setup-settings">
      <p className="settings__muted">Satış yaptığınız kanalları seçin. Kapalı bir kanalın menüsü gizlenir ve arka plan işleri durur; verileriniz silinmez. Tek tek modülleri Modüller sekmesinden de yönetebilirsiniz.</p>
      <ChannelPicker channels={setup.channels} selection={selection} onToggle={(id) => { setSelection((s) => toggleChannel(s, id)); setConfirming(false); setMessage(undefined) }} disabled={busy} />
      {message && <p className={message.ok ? 'setup-note' : 'setup-note setup-note--error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      {confirming && closing.length > 0 && (
        <section className="ps-card setup-settings__confirm" role="alertdialog" aria-label="Kanal kapatma onayı">
          <p>{closingNote(closing)}</p>
          <div className="setup-page__actions">
            <Button onClick={() => setConfirming(false)}>Vazgeç</Button>
            <Button variant="secondary" onClick={save} disabled={busy}>Kapat ve kaydet</Button>
          </div>
        </section>
      )}
      {!confirming && (
        <div className="setup-page__actions">
          <Button variant="primary" disabled={!dirty || busy} onClick={() => (closing.length > 0 ? setConfirming(true) : void save())}>{busy ? 'Kaydediliyor…' : 'Kaydet'}</Button>
        </div>
      )}
    </div>
  )
}
