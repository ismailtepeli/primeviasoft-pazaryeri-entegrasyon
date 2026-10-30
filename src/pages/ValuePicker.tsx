import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { ChannelValue } from '../api/types'

interface Props {
  channel: string
  categoryExternalId: number
  attributeId: number
  value: number | null
  /** Seçili değerin adı (liste henüz yüklenmediyse gösterilir). */
  valueName?: string | null
  onChange: (id: number | null) => void
  label: string
}

/** Kanal özelliğinin değer listesinden seçim; uzun listelerde ad araması yapar. */
export default function ValuePicker({ channel, categoryExternalId, attributeId, value, valueName, onChange, label }: Props) {
  const [query, setQuery] = useState('')
  const [term, setTerm] = useState('')
  const [options, setOptions] = useState<ChannelValue[]>([])

  useEffect(() => {
    const t = setTimeout(() => setTerm(query.trim()), 250)
    return () => clearTimeout(t)
  }, [query])

  useEffect(() => {
    let current = true
    const qs = term ? `?q=${encodeURIComponent(term)}` : ''
    api<ChannelValue[]>(`/catalog/${encodeURIComponent(channel)}/categories/${categoryExternalId}/attributes/${attributeId}/values${qs}`)
      .then((r) => { if (current) setOptions(r) })
      .catch(() => { if (current) setOptions([]) })
    return () => { current = false }
  }, [channel, categoryExternalId, attributeId, term])

  // Seçili değer arama sonucunda yoksa yine de seçenek olarak görünsün.
  const shown = value !== null && !options.some((o) => o.id === value) ? [{ id: value, name: valueName ?? String(value) }, ...options] : options

  return (
    <div className="value-picker">
      <input className="ps-input" type="search" placeholder="Değer ara" aria-label={`${label} için değer ara`} value={query} onChange={(e) => setQuery(e.target.value)} />
      <select className="ps-input" aria-label={label} value={value ?? ''} onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}>
        <option value="">Seçin…</option>
        {shown.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
      </select>
    </div>
  )
}
