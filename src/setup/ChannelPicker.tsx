import { Handshake, ShoppingBag, Store, type LucideIcon } from 'lucide-react'
import type { SalesChannel } from '../api/types'
import { StatusBadge } from '../components'
import { channelHint } from './channelSelection'
import './Setup.css'

const icons: Record<string, LucideIcon> = { marketplace: Store, b2b: Handshake, b2c: ShoppingBag }

interface Props {
  channels: SalesChannel[]
  selection: Set<string>
  onToggle: (id: string) => void
  disabled?: boolean
}

/** Üç satış kanalı kartı: pazaryerleri, B2B, B2C. Hazır olmayan kanal "Yakında" olarak görünür ve seçilemez. */
export function ChannelPicker({ channels, selection, onToggle, disabled }: Props) {
  return (
    <ul className="channels" aria-label="Satış kanalları">
      {channels.map((c) => {
        const Icon = icons[c.id] ?? Store
        const on = selection.has(c.id)
        const locked = !c.available || !!disabled
        return (
          <li key={c.id} className={`ps-card channel${on ? ' channel--on' : ''}${!c.available ? ' channel--soon' : ''}`}>
            <span className="channel__icon"><Icon size={24} aria-hidden /></span>
            <div className="channel__text">
              <h2>{c.name} {!c.available && <StatusBadge variant="inactive">Yakında</StatusBadge>}</h2>
              <p>{c.description}</p>
              {c.available && <p className="channel__hint">{channelHint[c.id]}</p>}
            </div>
            <button type="button" role="switch" aria-checked={on} aria-label={`${c.name} kanalı`} className="switch" disabled={locked} onClick={() => onToggle(c.id)}>
              <span className="switch__thumb" />
            </button>
          </li>
        )
      })}
    </ul>
  )
}
