import { useState } from 'react'
import { api } from '../api/client'
import type { DealerGroup } from '../api/types'
import { Button, TextInput } from '../components'
import './Dealers.css'

interface Props {
  groups: DealerGroup[]
  onChanged: () => void
}

/** Bayi grupları: ekle, yeniden adlandır, sil (bayisi olan grup silinemez). Fiyat listeleri bu gruplara bağlanır (#66). */
export default function DealerGroups({ groups, onChanged }: Props) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [editing, setEditing] = useState<string>()
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()

  async function submit() {
    if (!name.trim()) { setMessage({ ok: false, text: 'Grup adı boş olamaz.' }); return }
    try {
      const body = JSON.stringify({ name: name.trim(), description: description.trim() || null })
      if (editing) await api(`/dealer-groups/${editing}`, { method: 'PUT', body })
      else await api('/dealer-groups', { method: 'POST', body })
      setMessage({ ok: true, text: editing ? 'Grup kaydedildi.' : 'Grup eklendi.' })
      setName(''); setDescription(''); setEditing(undefined)
      onChanged()
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
  }

  async function remove(g: DealerGroup) {
    try {
      await api(`/dealer-groups/${g.id}`, { method: 'DELETE' })
      setMessage({ ok: true, text: `${g.name} silindi.` })
      onChanged()
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
  }

  return (
    <section className="ps-card dealer-editor" aria-labelledby="dealer-groups-title">
      <h2 id="dealer-groups-title" style={{ margin: 0, fontSize: 18 }}>Bayi grupları</h2>
      <p className="dealer-cell__meta" style={{ margin: 0 }}>Gruplar fiyat listesi ve iskonto bağlamak içindir (Fiyat listeleri modülü).</p>
      <div className="dealer-groups__form">
        <TextInput label="Grup adı" value={name} onChange={(e) => setName(e.target.value)} />
        <TextInput label="Açıklama" value={description} onChange={(e) => setDescription(e.target.value)} />
        <Button onClick={submit}>{editing ? 'Kaydet' : 'Grup ekle'}</Button>
      </div>
      {message && <p className={message.ok ? 'dealer-ok' : 'dealer-error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      {groups.length === 0 ? <p className="dealer-cell__meta">Henüz grup yok.</p> : (
        <ul className="dealer-groups__list">
          {groups.map((g) => (
            <li key={g.id} className="dealer-groups__item">
              <span className="dealer-cell"><strong>{g.name}</strong><span className="dealer-cell__meta">{g.description || 'Açıklama yok'} · {g.dealerCount} bayi</span></span>
              <span className="dealer-actions">
                <Button onClick={() => { setEditing(g.id); setName(g.name); setDescription(g.description ?? '') }}>Düzenle</Button>
                <Button onClick={() => remove(g)}>Sil</Button>
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
