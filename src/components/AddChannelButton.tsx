import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useChannels } from '../api/useChannels'
import { Button } from './Button'

/** "+ Kanal ekle": henüz bağlanmamış kanalları listeler; seçilen kanal ?kanal= ile Mağazalar'a gider. */
export function AddChannelButton() {
  const { channels } = useChannels()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const available = channels?.filter((c) => c.status === 'NotConnected') ?? []

  function pick(channel: string) {
    setOpen(false)
    navigate(`/magazalar?kanal=${encodeURIComponent(channel)}`)
  }

  return (
    <div className="add-channel">
      <Button variant="primary" onClick={() => setOpen(!open)} aria-haspopup="menu" aria-expanded={open}>+ Kanal ekle</Button>
      {open && (
        <ul className="add-channel__menu" role="menu">
          {available.length === 0 && <li className="add-channel__empty">Eklenecek kanal kalmadı.</li>}
          {available.map((c) => (
            <li key={c.channel} role="none">
              <button type="button" role="menuitem" onClick={() => pick(c.channel)}>{c.channel}</button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
