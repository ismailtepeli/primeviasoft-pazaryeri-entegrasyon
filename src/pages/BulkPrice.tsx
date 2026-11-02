import { useState } from 'react'
import { api } from '../api/client'
import type { PriceChangeResult, Product } from '../api/types'
import { Button, Select, TextInput } from '../components'
import { applyBulk, bulkSummary, modeLabel, validateBulk, type PriceMode } from './priceEdit'
import './ChannelForm.css'

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })

interface Props {
  productIds: string[]
  /** Önizleme örneği için seçili ürünlerden görünür olanlar. */
  sample?: Product
  onClose: () => void
  onApplied: (message: string) => void
}

/** Seçili ürünlere yüzde, tutar ya da sabit fiyat uygular; uygulamadan önce bir ürün üzerinde sonucu gösterir. */
export default function BulkPrice({ productIds, sample, onClose, onApplied }: Props) {
  const [mode, setMode] = useState<PriceMode>('percent')
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  const checked = text.trim() === '' ? null : validateBulk(mode, text)
  const next = checked && 'value' in checked && sample ? applyBulk(mode, checked.value, sample.price) : undefined

  async function apply() {
    const c = validateBulk(mode, text)
    if ('error' in c) { setError(c.error); return }
    setBusy(true)
    setError(undefined)
    try {
      const r = await api<PriceChangeResult>('/products/prices', { method: 'PUT', body: JSON.stringify({ productIds, mode, value: c.value }) })
      onApplied(bulkSummary(r))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Fiyatlar güncellenemedi.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="ps-card channel-form" style={{ maxWidth: 'none' }} aria-label="Toplu fiyat değiştir">
      <h2>{productIds.length} ürünün fiyatını değiştir</h2>
      <div className="products__bulk-price">
        <Select label="Değişiklik türü" value={mode} onChange={(e) => { setMode(e.target.value as PriceMode); setError(undefined) }}>
          {(Object.keys(modeLabel) as PriceMode[]).map((m) => <option key={m} value={m}>{modeLabel[m]}</option>)}
        </Select>
        <TextInput label={mode === 'percent' ? 'Yüzde (eksi azaltır)' : mode === 'amount' ? 'Tutar ₺ (eksi düşürür)' : 'Yeni fiyat ₺'} inputMode="decimal"
          value={text} error={checked && 'error' in checked ? checked.error : error} onChange={(e) => { setText(e.target.value); setError(undefined) }} />
      </div>
      {sample && next !== undefined && (
        <p className="channel-form__note" aria-live="polite">
          Örnek: <strong>{sample.name}</strong> {money.format(sample.price)} → {next === null ? 'geçersiz (sıfırdan büyük olmalı)' : <strong>{money.format(next)}</strong>}
        </p>
      )}
      <p className="channel-form__note">Fiyat kanallara bir sonraki stok-fiyat senkronunda yansır.</p>
      <div className="channel-form__actions">
        <Button onClick={onClose}>Vazgeç</Button>
        <Button variant="primary" onClick={apply} disabled={busy || checked === null || 'error' in (checked ?? {})}>{busy ? 'Uygulanıyor…' : 'Uygula'}</Button>
      </div>
    </section>
  )
}
