import { useState } from 'react'
import { api } from '../api/client'
import type { Product } from '../api/types'
import { Button, Select, TextInput } from '../components'
import { salePriceFor, saleModeLabel, saleSummary, validateSale, type SaleMode } from './saleEdit'
import './ChannelForm.css'

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })

interface Props {
  productIds: string[]
  /** Önizleme örneği için seçili ürünlerden biri. */
  sample?: Product
  onClose: () => void
  onApplied: (message: string) => void
}

/** Seçili ürünlere B2C kampanya fiyatı uygular ya da kaldırır; uygulamadan önce bir ürün üzerinde sonucu gösterir. */
export default function BulkSale({ productIds, sample, onClose, onApplied }: Props) {
  const [mode, setMode] = useState<SaleMode>('percent')
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  const checked = mode === 'clear' || text.trim() === '' ? null : validateSale(mode, text)
  const next = checked && 'value' in checked && sample ? salePriceFor(mode, checked.value, sample.price) : undefined
  const ready = mode === 'clear' || (checked !== null && 'value' in checked)

  async function apply() {
    const c = validateSale(mode, text)
    if ('error' in c) { setError(c.error); return }
    setBusy(true)
    setError(undefined)
    try {
      const r = await api<{ updated: number; cleared: number; skipped: string[] }>('/b2c/products/sale', { method: 'PUT', body: JSON.stringify({ productIds, mode, value: c.value }) })
      onApplied(saleSummary(r))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Kampanya uygulanamadı.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="ps-card channel-form" style={{ maxWidth: 'none' }} aria-label="B2C kampanya fiyatı">
      <h2>{productIds.length} ürün için B2C kampanyası</h2>
      <div className="products__bulk-price">
        <Select label="Kampanya türü" value={mode} onChange={(e) => { setMode(e.target.value as SaleMode); setError(undefined) }}>
          {(Object.keys(saleModeLabel) as SaleMode[]).map((m) => <option key={m} value={m}>{saleModeLabel[m]}</option>)}
        </Select>
        {mode !== 'clear' && (
          <TextInput label={mode === 'percent' ? 'İndirim yüzdesi (%)' : 'Kampanya fiyatı ₺ (KDV dahil)'} inputMode="decimal" value={text}
            error={checked && 'error' in checked ? checked.error : error} onChange={(e) => { setText(e.target.value); setError(undefined) }} />
        )}
      </div>
      {sample && next !== undefined && (
        <p className="channel-form__note" aria-live="polite">
          Örnek: <strong>{sample.name}</strong> {money.format(sample.price)} → {next === null ? 'uygulanamaz (kampanya fiyatı normal fiyattan düşük olmalı)' : <strong>{money.format(next)}</strong>}
        </p>
      )}
      <p className="channel-form__note">Kampanya yalnızca B2C mağazada geçerlidir; mağazada normal fiyat üstü çizili ve indirim yüzdesiyle gösterilir.</p>
      {mode === 'clear' && error && <p className="channel-form__error" role="alert">{error}</p>}
      <div className="channel-form__actions">
        <Button onClick={onClose}>Vazgeç</Button>
        <Button variant="primary" onClick={apply} disabled={busy || !ready}>{busy ? 'Uygulanıyor…' : mode === 'clear' ? 'Kampanyayı kaldır' : 'Uygula'}</Button>
      </div>
    </section>
  )
}
