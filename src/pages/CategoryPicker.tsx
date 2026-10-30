import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { ChannelCategory } from '../api/types'
import { Button, TextInput } from '../components'
import './CategoryPicker.css'

interface Props {
  channel: string
  title: string
  onPick: (category: ChannelCategory) => void
  onClose: () => void
}

/** Kanal kategorisi seçici: arama (en az 2 harf) ya da kökten gezinme. Yalnızca en alt seviye (yaprak) seçilebilir. */
export default function CategoryPicker({ channel, title, onPick, onClose }: Props) {
  const [query, setQuery] = useState('')
  const [term, setTerm] = useState('')
  const [trail, setTrail] = useState<ChannelCategory[]>([])
  const [items, setItems] = useState<ChannelCategory[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()

  useEffect(() => {
    const t = setTimeout(() => setTerm(query.trim().length >= 2 ? query.trim() : ''), 300)
    return () => clearTimeout(t)
  }, [query])

  const parent = trail[trail.length - 1]
  useEffect(() => {
    let current = true
    setLoading(true)
    const qs = term ? `q=${encodeURIComponent(term)}` : parent ? `parentId=${parent.externalId}` : ''
    api<ChannelCategory[]>(`/catalog/${encodeURIComponent(channel)}/categories?${qs}`)
      .then((r) => { if (current) { setItems(r); setError(undefined) } })
      .catch((e: Error) => { if (current) setError(e.message) })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [channel, term, parent])

  return (
    <section className="ps-card category-picker" aria-labelledby="picker-title">
      <h2 id="picker-title">{title}</h2>
      <TextInput label="Kanal kategorisi ara" placeholder="En az 2 harf" value={query} onChange={(e) => setQuery(e.target.value)} />

      {!term && (
        <nav className="category-picker__trail" aria-label="Konum">
          <button type="button" onClick={() => setTrail([])} disabled={trail.length === 0}>Tüm kategoriler</button>
          {trail.map((c, i) => (
            <span key={c.externalId}>
              {' › '}
              <button type="button" onClick={() => setTrail(trail.slice(0, i + 1))} disabled={i === trail.length - 1}>{c.name}</button>
            </span>
          ))}
        </nav>
      )}

      {error && <p className="channel-form__error" role="alert">{error}</p>}
      {!error && !loading && items.length === 0 && (
        <p className="channel-form__note">
          {term ? 'Eşleşen kategori yok.' : 'Kategori yok. Kataloğun senkronlandığından emin olun.'}
        </p>
      )}
      <ul className="category-picker__list">
        {items.map((c) => (
          <li key={c.externalId}>
            <span>
              <strong>{c.name}</strong>
              {term && <span className="category-picker__path">{c.path}</span>}
            </span>
            {c.isLeaf
              ? <Button onClick={() => onPick(c)} aria-label={`${c.path} kategorisini seç`}>Seç</Button>
              : <Button onClick={() => { setQuery(''); setTrail([...trail, c]) }} aria-label={`${c.name} altına gir`}>Alt kategoriler →</Button>}
          </li>
        ))}
      </ul>
      <p className="channel-form__note">Ürünler yalnızca en alt seviye kategoriye aktarılabilir; üst kategoriler seçilemez.</p>
      <div className="channel-form__actions"><Button onClick={onClose}>Vazgeç</Button></div>
    </section>
  )
}
