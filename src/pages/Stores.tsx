import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import type { ChannelState, ChannelStatus } from '../api/types'
import { useChannels } from '../api/useChannels'
import { Button, StatusBadge, type BadgeVariant } from '../components'
import { timeAgo } from '../lib/timeAgo'
import ChannelForm from './ChannelForm'
import './Stores.css'

const badge: Record<ChannelState, { text: string; variant: BadgeVariant }> = {
  Connected: { text: 'Bağlı', variant: 'success' },
  AuthError: { text: 'Hata', variant: 'error' },
  NotConnected: { text: 'Bağla', variant: 'inactive' },
}

const action: Record<ChannelState, string> = {
  Connected: 'Yönet',
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
  const { channels, error } = useChannels()
  const [params] = useSearchParams()
  const selected = params.get('kanal')
  const navigate = useNavigate()
  const [notice, setNotice] = useState<string>()

  if (error) return <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>
  if (!channels) return <p style={{ color: 'var(--ink-muted)' }}>Yükleniyor…</p>

  return (
    <>
      {selected && (
        <ChannelForm key={selected} channel={selected} onCancel={() => navigate('/magazalar')}
          onSubmit={() => setNotice('Bilgiler doğrulandı; kaydetme henüz etkin değil.')} />
      )}
      {notice && <p role="status" className="store-notice">{notice}</p>}
      <div className="store-grid">
        {channels.map((c) => <ChannelCard key={c.channel} channel={c} selected={c.channel === selected} />)}
      </div>
    </>
  )
}
