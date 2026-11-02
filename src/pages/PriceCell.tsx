import { useEffect, useRef, useState } from 'react'
import { Pencil } from 'lucide-react'
import { api } from '../api/client'
import { validateSinglePrice } from './priceEdit'

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })

interface Props {
  productId: string
  name: string
  price: number
  /** Kaydedilince yeni fiyatla çağrılır; liste yeniden yüklenmeden satır güncellenir. */
  onSaved: (price: number) => void
}

/** Tabloda fiyatı satır içinde düzenler: tıkla, yaz, Enter ile kaydet, Esc ile vazgeç. */
export function PriceCell({ productId, name, price, onSaved }: Props) {
  const [editing, setEditing] = useState(false)
  const [text, setText] = useState('')
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const input = useRef<HTMLInputElement>(null)
  const done = useRef(false) // Enter/Esc sonrası blur ikinci kez kaydetmesin

  useEffect(() => { if (editing) input.current?.select() }, [editing])

  function start() {
    done.current = false
    setText(String(price).replace('.', ','))
    setError(undefined)
    setEditing(true)
  }

  async function commit() {
    if (done.current) return
    const checked = validateSinglePrice(text)
    if ('error' in checked) { setError(checked.error); return }
    if (checked.price === price) { done.current = true; setEditing(false); return }
    done.current = true
    setBusy(true)
    try {
      const r = await api<{ id: string; price: number }>(`/products/${productId}/price`, { method: 'PUT', body: JSON.stringify({ price: checked.price }) })
      onSaved(r.price)
      setEditing(false)
    } catch (e) {
      done.current = false
      setError(e instanceof Error ? e.message : 'Kaydedilemedi.')
    } finally {
      setBusy(false)
    }
  }

  if (!editing) {
    return (
      <button type="button" className="price-cell" onClick={start} aria-label={`${name} fiyatını düzenle, şu an ${money.format(price)}`}>
        <span>{money.format(price)}</span><Pencil size={14} aria-hidden />
      </button>
    )
  }
  return (
    <div className="price-cell price-cell--editing">
      <input ref={input} className="ps-input price-cell__input" inputMode="decimal" aria-label={`${name} yeni fiyat`} aria-invalid={!!error}
        value={text} disabled={busy} onChange={(e) => { setText(e.target.value); setError(undefined) }}
        onKeyDown={(e) => {
          if (e.key === 'Enter') { e.preventDefault(); void commit() }
          if (e.key === 'Escape') { done.current = true; setEditing(false) }
        }}
        onBlur={() => { void commit() }} />
      {error && <span className="price-cell__error" role="alert">{error}</span>}
    </div>
  )
}
