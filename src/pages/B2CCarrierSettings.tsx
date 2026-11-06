import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import { Button, StatusBadge, TextInput } from '../components'
import { MAX_RATES, TRACKING_PLACEHOLDER, carrierSummary, emptyForm, toForm, validateCarrier, type Carrier, type CarrierForm } from './b2cCarriers'
import './PriceLists.css'

/** Ayarlar > B2C mağaza: kargo firmaları, desi ücret kuralları ve takip bağlantısı. Firma tanımlı değilse mağaza yukarıdaki tek kargo ücretini kullanır. */
export default function B2CCarrierSettings() {
  const [carriers, setCarriers] = useState<Carrier[]>()
  const [form, setForm] = useState<CarrierForm>()
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [busy, setBusy] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<string>()

  const load = useCallback(() => {
    api<Carrier[]>('/b2c/carriers').then(setCarriers).catch((e: Error) => setMessage({ ok: false, text: e.message }))
  }, [])
  useEffect(load, [load])

  async function save() {
    if (!form) return
    const result = validateCarrier(form, (carriers ?? []).filter((c) => c.id !== form.id).map((c) => c.name))
    if ('error' in result) { setMessage({ ok: false, text: result.error }); return }
    setBusy(true)
    try {
      await api(form.id ? `/b2c/carriers/${form.id}` : '/b2c/carriers', { method: form.id ? 'PUT' : 'POST', body: JSON.stringify(result.body) })
      setMessage({ ok: true, text: 'Kargo firması kaydedildi.' }); setForm(undefined); load()
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
    finally { setBusy(false) }
  }

  async function remove(id: string) {
    setBusy(true)
    try { await api(`/b2c/carriers/${id}`, { method: 'DELETE' }); setMessage({ ok: true, text: 'Kargo firması silindi.' }); setConfirmDelete(undefined); load() }
    catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
    finally { setBusy(false) }
  }

  const set = <K extends keyof CarrierForm>(key: K, value: CarrierForm[K]) => form && setForm({ ...form, [key]: value })
  const setRate = (i: number, key: 'upToDesi' | 'fee', value: string) => form && setForm({ ...form, rates: form.rates.map((r, j) => (j === i ? { ...r, [key]: value } : r)) })

  return (
    <section className="ps-card price-editor" aria-labelledby="b2c-carrier-title">
      <div className="price-editor__head">
        <h2 id="b2c-carrier-title">Kargo firmaları</h2>
        {!form && <Button onClick={() => { setForm(emptyForm()); setMessage(undefined) }}>Firma ekle</Button>}
      </div>
      <p className="price-cell__meta" style={{ margin: 0 }}>
        Firma eklerseniz müşteri ödeme sayfasında firmayı seçer ve ücret firmaya göre hesaplanır; hiç firma yoksa yukarıdaki tek kargo ücreti uygulanır.
        Ücretsiz kargo tutarı tüm firmalar için geçerlidir. Desi, ürünün kanal verisindeki desi değerinden gelir; girilmemişse 0 sayılır.
      </p>

      {carriers === undefined && !message && <p className="price-cell__meta" aria-busy="true">Yükleniyor…</p>}
      {carriers?.length === 0 && !form && <p className="price-cell__meta">Henüz kargo firması yok.</p>}
      {carriers && carriers.length > 0 && (
        <ul className="carrier-list">
          {carriers.map((c) => (
            <li key={c.id} className="carrier-row">
              <div className="carrier-row__main">
                <strong>{c.name}</strong> <StatusBadge variant={c.enabled ? 'success' : 'inactive'}>{c.enabled ? 'Açık' : 'Kapalı'}</StatusBadge>
                <span className="price-cell__meta">{carrierSummary(c)}</span>
              </div>
              <div className="carrier-row__actions">
                {confirmDelete === c.id ? (
                  <>
                    <span className="price-cell__meta">Silinsin mi? Verilmiş siparişler etkilenmez.</span>
                    <Button onClick={() => remove(c.id)} disabled={busy}>Evet, sil</Button>
                    <Button onClick={() => setConfirmDelete(undefined)}>Vazgeç</Button>
                  </>
                ) : (
                  <>
                    <Button onClick={() => { setForm(toForm(c)); setMessage(undefined) }} aria-label={`${c.name} firmasını düzenle`}>Düzenle</Button>
                    <Button onClick={() => setConfirmDelete(c.id)} aria-label={`${c.name} firmasını sil`}>Sil</Button>
                  </>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      {form && (
        <fieldset className="price-pay">
          <legend>{form.id ? 'Firmayı düzenle' : 'Yeni kargo firması'}</legend>
          <div className="price-form">
            <TextInput label="Firma adı" value={form.name} maxLength={60} onChange={(e) => set('name', e.target.value)} />
            {form.rates.length === 0 && <TextInput label="Sabit kargo ücreti (₺)" hint="Desi aralığı eklemezseniz her sepet için bu ücret uygulanır" inputMode="decimal" value={form.flatFee} onChange={(e) => set('flatFee', e.target.value)} />}
          </div>
          <TextInput label="Takip bağlantısı (isteğe bağlı)" hint={`Örn. https://kargo.example.com/takip?no=${TRACKING_PLACEHOLDER}. Müşteri sipariş detayında "Kargom nerede?" bağlantısını görür.`} value={form.trackingUrlTemplate} onChange={(e) => set('trackingUrlTemplate', e.target.value)} />
          <label className="price-cat-row"><input type="checkbox" checked={form.enabled} onChange={(e) => set('enabled', e.target.checked)} /> Mağazada sun</label>

          <div className="carrier-rates">
            <h3>Desi aralıkları</h3>
            <p className="price-cell__meta" style={{ margin: 0 }}>Sepet desisi, uyan en küçük sınırın ücretini öder. En büyük sınırı aşan sepete bu firma sunulmaz.</p>
            {form.rates.map((r, i) => (
              <div key={i} className="carrier-rate">
                <TextInput label="En çok desi" inputMode="decimal" value={r.upToDesi} onChange={(e) => setRate(i, 'upToDesi', e.target.value)} />
                <TextInput label="Ücret (₺)" inputMode="decimal" value={r.fee} onChange={(e) => setRate(i, 'fee', e.target.value)} />
                <Button onClick={() => set('rates', form.rates.filter((_, j) => j !== i))} aria-label={`${i + 1}. aralığı kaldır`}>Kaldır</Button>
              </div>
            ))}
            {form.rates.length < MAX_RATES && <div><Button onClick={() => set('rates', [...form.rates, { upToDesi: '', fee: '' }])}>Aralık ekle</Button></div>}
          </div>

          <div className="price-editor__actions">
            <Button onClick={() => { setForm(undefined); setMessage(undefined) }}>Vazgeç</Button>
            <Button onClick={save} disabled={busy}>{busy ? 'Kaydediliyor…' : 'Firmayı kaydet'}</Button>
          </div>
        </fieldset>
      )}

      {message && <p className={message.ok ? 'price-ok' : 'price-error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
    </section>
  )
}
