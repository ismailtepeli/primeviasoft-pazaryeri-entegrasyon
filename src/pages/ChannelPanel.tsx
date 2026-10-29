import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { ChannelState, ChannelStatus } from '../api/types'
import { StatusBadge, type BadgeVariant } from '../components'
import { timeAgo } from '../lib/timeAgo'
import './ChannelPanel.css'

const labels: Record<ChannelState, { text: string; variant: BadgeVariant }> = {
  Connected: { text: 'Bağlı', variant: 'success' },
  AuthError: { text: 'Kimlik hatası', variant: 'error' },
  NotConnected: { text: 'Bağlanmadı', variant: 'inactive' },
}

export function ChannelRow({ channel }: { channel: ChannelStatus }) {
  const { text, variant } = labels[channel.status]
  return (
    <li className="channel">
      <div className="channel__main">
        <span className="channel__name">{channel.channel}</span>
        <StatusBadge variant={variant}>{text}</StatusBadge>
      </div>
      <div className="channel__meta">
        {channel.status === 'Connected' && (channel.lastSyncAt ? `Son senkron: ${timeAgo(channel.lastSyncAt)}` : 'Henüz senkron yapılmadı')}
        {channel.status === 'AuthError' && <Link to="/magazalar">API bilgilerini yenile</Link>}
        {channel.status === 'NotConnected' && <Link to="/magazalar">Mağazalar ekranında bağla</Link>}
      </div>
    </li>
  )
}

export default function ChannelPanel() {
  const [channels, setChannels] = useState<ChannelStatus[]>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    api<ChannelStatus[]>('/dashboard/channels')
      .then(setChannels)
      .catch((e: Error) => setError(e.message))
  }, [])

  return (
    <section className="ps-card" aria-labelledby="channels-title">
      <h2 id="channels-title" className="channel-panel__title">Kanal durumu</h2>
      {error && <p className="channel-panel__error" role="alert">{error}</p>}
      {!error && !channels && <p className="channel-panel__loading">Yükleniyor…</p>}
      {channels && <ul className="channel-list">{channels.map((c) => <ChannelRow key={c.channel} channel={c} />)}</ul>}
    </section>
  )
}
