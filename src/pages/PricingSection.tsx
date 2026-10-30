import { useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { ApplyPricingResult, PricingRuleDto } from '../api/types'
import { Button, Select, TextInput } from '../components'
import { ROUNDING_LABELS, calculatePrice, validateRule, type PriceRounding } from '../lib/priceCalculator'
import './ChannelForm.css'

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })
const num = (s: string) => Number(s.replace(',', '.'))

interface Form { commission: string; shipping: string; profit: string; rounding: PriceRounding }

const toForm = (r: PricingRuleDto): Form => ({
  commission: String(r.commissionPercent), shipping: String(r.shippingCost), profit: String(r.profitPercent), rounding: r.rounding as PriceRounding,
})

/** Kanal başına fiyat kuralı: komisyon, kargo, kâr, yuvarlama; örnek maliyetle anlık önizleme ve toplu uygulama. */
export default function PricingSection({ channel }: { channel: string }) {
  const [form, setForm] = useState<Form>({ commission: '0', shipping: '0', profit: '0', rounding: 'None' })
  const [saved, setSaved] = useState(false)
  const [sampleCost, setSampleCost] = useState('100')
  const [loading, setLoading] = useState(true)
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<PricingRuleDto[]>('/pricing/rules')
      .then((rules) => {
        const rule = rules.find((r) => r.channel === channel)
        if (rule) { setForm(toForm(rule)); setSaved(rule.saved) }
      })
      .catch((e: Error) => setMessage({ ok: false, text: e.message }))
      .finally(() => setLoading(false))
  }, [channel])

  const rule = useMemo(() => ({ commissionPercent: num(form.commission), shippingCost: num(form.shipping), profitPercent: num(form.profit), rounding: form.rounding }), [form])
  const ruleError = form.commission.trim() === '' || form.shipping.trim() === '' || form.profit.trim() === '' ? 'Komisyon, kargo ve kâr alanlarını doldurun.' : validateRule(rule)
  const preview = useMemo(() => (ruleError ? null : calculatePrice(num(sampleCost), rule)), [ruleError, sampleCost, rule])
  const set = (key: keyof Form) => (e: { target: { value: string } }) => { setForm((f) => ({ ...f, [key]: e.target.value })); setMessage(undefined) }

  async function save() {
    setBusy(true)
    setMessage(undefined)
    try {
      await api(`/pricing/rules/${encodeURIComponent(channel)}`, {
        method: 'PUT',
        body: JSON.stringify({ commissionPercent: rule.commissionPercent, shippingCost: rule.shippingCost, profitPercent: rule.profitPercent, rounding: rule.rounding }),
      })
      setSaved(true)
      setMessage({ ok: true, text: `${channel} fiyat kuralı kaydedildi.` })
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Kaydedilemedi.' })
    } finally {
      setBusy(false)
    }
  }

  async function apply() {
    setBusy(true)
    setMessage(undefined)
    try {
      const r = await api<ApplyPricingResult>(`/pricing/apply/${encodeURIComponent(channel)}`, { method: 'POST' })
      const parts = [`${r.updated} ürünün fiyatı güncellendi`, `${r.unchanged} ürün zaten güncel`]
      if (r.noCost > 0) parts.push(`${r.noCost} ürün maliyeti girilmediği için hesaplanamadı`)
      setMessage({ ok: true, text: parts.join(', ') + '.' })
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Kurallar uygulanamadı.' })
    } finally {
      setBusy(false)
    }
  }

  if (loading) return <p>Yükleniyor…</p>

  return (
    <section className="ps-card pricing" aria-labelledby="pricing-title">
      <h2 id="pricing-title">{channel} fiyat kuralı</h2>
      <p className="pricing__formula">Satış fiyatı = (maliyet + kargo) × (1 + kâr) ÷ (1 − komisyon), sonra yuvarlama.</p>

      <div className="pricing__grid">
        <TextInput label="Komisyon (%)" type="number" min={0} max={99.99} step="0.01" value={form.commission} onChange={set('commission')} />
        <TextInput label="Kargo (₺)" type="number" min={0} step="0.01" value={form.shipping} onChange={set('shipping')} />
        <TextInput label="Kâr (%)" type="number" min={0} max={1000} step="0.01" value={form.profit} onChange={set('profit')} />
        <Select label="Yuvarlama" value={form.rounding} onChange={set('rounding')}>
          {(Object.keys(ROUNDING_LABELS) as PriceRounding[]).map((k) => <option key={k} value={k}>{ROUNDING_LABELS[k]}</option>)}
        </Select>
      </div>

      <div className="pricing__preview" aria-live="polite">
        <TextInput label="Örnek maliyet (₺)" type="number" min={0} step="0.01" value={sampleCost} onChange={(e) => setSampleCost(e.target.value)} />
        {ruleError && <p className="channel-form__error" role="alert">{ruleError}</p>}
        {!ruleError && !preview && <p className="channel-form__note">Önizleme için sıfırdan büyük bir maliyet girin.</p>}
        {preview && (
          <dl className="pricing__breakdown">
            <div><dt>Maliyet + kargo</dt><dd>{money.format(preview.base)}</dd></div>
            <div><dt>Kâr eklenmiş</dt><dd>{money.format(preview.withProfit)}</dd></div>
            <div><dt>Komisyon payı eklenmiş</dt><dd>{money.format(preview.beforeRounding)}</dd></div>
            <div className="pricing__result"><dt>Satış fiyatı</dt><dd>{money.format(preview.price)}</dd></div>
            <div><dt>Komisyon tutarı</dt><dd>{money.format(preview.commissionAmount)}</dd></div>
            <div><dt>Net kâr</dt><dd>{money.format(preview.netProfit)}</dd></div>
          </dl>
        )}
      </div>

      {message && <p className={message.ok ? 'channel-form__ok' : 'channel-form__error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      <div className="channel-form__actions">
        <Button onClick={apply} disabled={busy || !saved} title={saved ? undefined : 'Önce kuralı kaydedin'}>Kuralları uygula</Button>
        <Button variant="primary" onClick={save} disabled={busy || !!ruleError}>Kuralı kaydet</Button>
      </div>
      <p className="channel-form__note">
        "Kuralları uygula" maliyeti girilmiş tüm ürünlerin {channel} satış fiyatını hesaplar; gönderimde katalog fiyatı yerine bu fiyat kullanılır.
        Maliyeti ürün sayfasından ya da Excel'deki "Maliyet" sütunundan girersiniz.
      </p>
    </section>
  )
}
