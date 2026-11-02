import { useEffect, useState } from 'react'
import { api } from '../api/client'
import { Button, TextInput } from '../components'
import { MAX_INFO, toForm, validateSettings, type B2CSettings, type B2CSettingsForm } from './b2cSettings'
import './PriceLists.css'

/** Ayarlar > B2C mağaza: kargo ücreti, ücretsiz kargo eşiği ve ödeme yöntemleri (havale, kapıda ödeme, kart). */
export default function B2CCheckoutSettings() {
  const [form, setForm] = useState<B2CSettingsForm>()
  const [cardProvider, setCardProvider] = useState(false)
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<B2CSettings>('/b2c/settings').then((s) => { setForm(toForm(s)); setCardProvider(s.cardProviderAvailable) })
      .catch((e: Error) => setMessage({ ok: false, text: e.message }))
  }, [])

  if (!form) return message ? <p className="price-error" role="alert">{message.text}</p> : <p className="price-cell__meta" aria-busy="true">Yükleniyor…</p>
  const set = <K extends keyof B2CSettingsForm>(key: K, value: B2CSettingsForm[K]) => setForm({ ...form, [key]: value })

  async function save() {
    if (!form) return
    const result = validateSettings(form)
    if ('error' in result) { setMessage({ ok: false, text: result.error }); return }
    setBusy(true)
    try {
      const saved = await api<B2CSettings>('/b2c/settings', { method: 'PUT', body: JSON.stringify(result.body) })
      setForm(toForm(saved)); setCardProvider(saved.cardProviderAvailable)
      setMessage({ ok: true, text: 'Kargo ve ödeme ayarları kaydedildi.' })
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
    finally { setBusy(false) }
  }

  return (
    <section className="ps-card price-editor" aria-labelledby="b2c-pay-title">
      <h2 id="b2c-pay-title" style={{ margin: 0, fontSize: 18 }}>Kargo ve ödeme</h2>
      <p className="price-cell__meta" style={{ margin: 0 }}>Müşteri sepette bu ücretleri ve açık ödeme yöntemlerini görür. Tutarlar KDV dahildir.</p>
      <div className="price-form">
        <TextInput label="Kargo ücreti (₺)" inputMode="decimal" value={form.shippingFee} onChange={(e) => set('shippingFee', e.target.value)} />
        <TextInput label="Ücretsiz kargo tutarı (₺)" hint="Ara toplam bu tutara ulaşınca kargo ücretsiz olur; boşsa her sipariş kargo ücretlidir" inputMode="decimal" value={form.freeShippingThreshold} onChange={(e) => set('freeShippingThreshold', e.target.value)} />
      </div>

      <fieldset className="price-pay">
        <legend>Ödeme yöntemleri</legend>
        <label className="price-cat-row"><input type="checkbox" checked={form.bankTransferEnabled} onChange={(e) => set('bankTransferEnabled', e.target.checked)} /> Havale / EFT</label>
        {form.bankTransferEnabled && (
          <label className="field">
            <span className="field__label">Müşterinin göreceği banka bilgisi</span>
            <textarea className="ps-input" rows={4} maxLength={MAX_INFO} value={form.bankTransferInfo} onChange={(e) => set('bankTransferInfo', e.target.value)} />
            <span className="field__hint">Banka, hesap sahibi ve IBAN. Sipariş sonrası ve sipariş detayında gösterilir.</span>
          </label>
        )}
        <label className="price-cat-row"><input type="checkbox" checked={form.cashOnDeliveryEnabled} onChange={(e) => set('cashOnDeliveryEnabled', e.target.checked)} /> Kapıda ödeme</label>
        {form.cashOnDeliveryEnabled && <div className="price-form"><TextInput label="Kapıda ödeme ücreti (₺)" inputMode="decimal" value={form.cashOnDeliveryFee} onChange={(e) => set('cashOnDeliveryFee', e.target.value)} /></div>}
        <label className="price-cat-row"><input type="checkbox" checked={form.cardEnabled} disabled={!cardProvider && !form.cardEnabled} onChange={(e) => set('cardEnabled', e.target.checked)} /> Kredi / banka kartı</label>
        {!cardProvider && <p className="price-cell__meta" style={{ margin: 0 }}>Kart ödemesi için bir ödeme sağlayıcısı (iyzico, PayTR, Stripe) yapılandırılmalıdır; henüz bağlı değil.</p>}
      </fieldset>

      {message && <p className={message.ok ? 'price-ok' : 'price-error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      <div className="price-editor__actions"><Button onClick={save} disabled={busy}>{busy ? 'Kaydediliyor…' : 'Kaydet'}</Button></div>
    </section>
  )
}
