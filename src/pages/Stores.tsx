import { useState } from 'react'
import { api } from '../api/client'
import type { ConnectionTestResult } from '../api/types'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import type { ChannelState, ChannelStatus } from '../api/types'
import { useChannels } from '../api/useChannels'
import { Button, StatusBadge, type BadgeVariant } from '../components'
import { timeAgo } from '../lib/timeAgo'
import ChannelForm from './ChannelForm'
import './Stores.css'

const badge: Record<ChannelState, { text: string; variant: BadgeVariant }> = {
  Connected: { text: 'Bağlı', variant: 'success' },
  Unverified: { text: 'Doğrulanmadı', variant: 'warning' },
  AuthError: { text: 'Hata', variant: 'error' },
  NotConnected: { text: 'Bağla', variant: 'inactive' },
}

const action: Record<ChannelState, string> = {
  Connected: 'Yönet',
  Unverified: 'Test et',
  AuthError: 'Yeniden bağla',
  NotConnected: 'Bağla',
}

export function ChannelCard({ channel, selected }: { channel: ChannelStatus; selected: boolean }) {
  const { text, variant } = badge[channel.status]
  return (
    <article className="ps-card store-card" aria-current={selected ? 'true' : undefined}>
      <header className="store-card__head">
        <h2>{channel.channel}</h2>
        <StatusBadge variant={variant}>{text}</StatusBadge>
      </header>
      <p className="store-card__meta">
        {channel.status === 'Connected' && (channel.lastSyncAt ? `Son senkron: ${timeAgo(channel.lastSyncAt)}` : 'Henüz senkron yapılmadı')}
        {channel.status === 'Unverified' && 'Bilgiler kayıtlı ama henüz doğrulanmadı.'}
        {channel.status === 'AuthError' && 'API bilgileri reddedildi; bilgileri yenileyin.'}
        {channel.status === 'NotConnected' && 'Bu kanal henüz bağlanmadı.'}
      </p>
      <Link to={`/magazalar?kanal=${encodeURIComponent(channel.channel)}`} tabIndex={-1}>
        <Button>{action[channel.status]}</Button>
      </Link>
    </article>
  )
}

export default function Stores() {
  const { channels, error, reload } = useChannels()
  const [params] = useSearchParams()
  const selected = params.get('kanal')
  const navigate = useNavigate()
  const [notice, setNotice] = useState<{ ok: boolean; text: string }>()
  const [formVersion, setFormVersion] = useState(0)

  async function save(channel: string, values: Record<string, string>) {
    const { environment, ...fields } = values
    // Boş gizli alan gönderilmez; sunucu kayıtlı değeri korur.
    const body = { environment: environment === 'staging' ? 'Staging' : 'Live', fields: Object.fromEntries(Object.entries(fields).filter(([, v]) => v.trim() !== '')) }
    await api(`/channels/${encodeURIComponent(channel)}/credentials`, { method: 'PUT', body: JSON.stringify(body) })
    setFormVersion((v) => v + 1) // formu yeniden kur: yazılan gizli değerler silinir, maskeli hali gelir
    // Kaydedilen bilgi "bağlı" sayılmaz; hemen kanalda doğrulanır ve sonuç kullanıcıya gösterilir.
    const test = await api<ConnectionTestResult>(`/channels/${encodeURIComponent(channel)}/test`, { method: 'POST' })
    setNotice({ ok: test.success, text: `${channel} bilgileri şifrelenerek kaydedildi. ${test.message}` })
    reload()
  }

  if (error) return <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>
  if (!channels) return <p style={{ color: 'var(--ink-muted)' }}>Yükleniyor…</p>

  return (
    <>
      {selected && (
        <ChannelForm key={`${selected}-${formVersion}`} channel={selected} onCancel={() => navigate('/magazalar')}
          onSubmit={(values) => save(selected, values)} onTested={reload} />
      )}
      {notice && <p role="status" className={`store-notice store-notice--${notice.ok ? 'ok' : 'warn'}`}>{notice.text}</p>}
      <div className="store-grid">
        {channels.map((c) => <ChannelCard key={c.channel} channel={c} selected={c.channel === selected} />)}
      </div>
    </>
  )
}
