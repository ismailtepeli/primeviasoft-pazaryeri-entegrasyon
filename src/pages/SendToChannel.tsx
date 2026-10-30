import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { ProductSendResult } from '../api/types'
import { useChannels } from '../api/useChannels'
import { Button, Select } from '../components'
import { describeSend, sendWasBlocked, sendableChannels } from './sendHelpers'
import './ChannelForm.css'

/** Seçili ürünleri bağlı bir kanala göndermek için kuyruğa alır; sonuç ürünlerin kanal durumuna sonradan yansır. */
export default function SendToChannel({ productIds, onClose, onSent }: { productIds: string[]; onClose: () => void; onSent: () => void }) {
  const { channels, error: channelsError } = useChannels()
  const [channel, setChannel] = useState('')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()

  const options = sendableChannels(channels ?? [])
  const selected = channel || options[0]?.channel || ''

  async function send() {
    setBusy(true)
    setMessage(undefined)
    try {
      const result = await api<ProductSendResult>('/products/send', { method: 'POST', body: JSON.stringify({ channel: selected, productIds }) })
      setMessage({ ok: !sendWasBlocked(result), text: describeSend(selected, result) })
      onSent()
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Gönderim başlatılamadı.' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="ps-card channel-form" aria-labelledby="send-title">
      <h2 id="send-title">{productIds.length} ürünü kanala gönder</h2>
      {channelsError && <p className="channel-form__error" role="alert">{channelsError}</p>}
      {channels && options.length === 0 && (
        <p className="channel-form__error" role="alert">
          Ürün gönderebileceğiniz bağlı kanal yok. <Link to="/magazalar">Mağazalar</Link> ekranından Trendyol'u bağlayıp bağlantıyı test edin.
        </p>
      )}
      {options.length > 0 && (
        <Select label="Kanal" value={selected} onChange={(e) => setChannel(e.target.value)}>
          {options.map((c) => <option key={c.channel} value={c.channel}>{c.channel}</option>)}
        </Select>
      )}
      <p className="channel-form__note">
        Eksik bilgisi olan ürünler (marka/kategori eşlemesi, açıklama, görsel) gönderilmez; "Eksik" durumunda nedenleriyle listelenir.
        Kanalın sonucu birkaç dakika içinde ürün durumuna yansır.
      </p>
      {message && <p className={message.ok ? 'channel-form__ok' : 'channel-form__error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      <div className="channel-form__actions">
        <Button onClick={onClose}>Kapat</Button>
        <Button onClick={send} disabled={busy || !selected || (message?.ok ?? false)}>{busy ? 'Gönderiliyor…' : 'Gönder'}</Button>
      </div>
    </section>
  )
}
