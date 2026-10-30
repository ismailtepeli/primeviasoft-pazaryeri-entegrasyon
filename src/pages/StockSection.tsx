import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { StockPage, StockRow, StockRuleDto, StockSettings } from '../api/types'
import { Button, DataTable, TextInput, type Column } from '../components'
import { channelQuantity, effectiveSafety, validateStockRule } from '../lib/stockCalculator'
import './ChannelForm.css'

const PAGE_SIZE = 15

/** Kanal stok kuralı (güvenlik stoğu, açılan pay), ortak havuz ayarı ve ürün bazında dağıtım tablosu. */
export default function StockSection({ channel }: { channel: string }) {
  const [safety, setSafety] = useState('0')
  const [percent, setPercent] = useState('100')
  const [saved, setSaved] = useState(false)
  const [deduct, setDeduct] = useState(true)
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [busy, setBusy] = useState(false)

  const [search, setSearch] = useState('')
  const [term, setTerm] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<StockPage>({ channel, items: [], total: 0 })
  const [reload, setReload] = useState(0)

  useEffect(() => {
    Promise.all([api<StockRuleDto[]>('/stock/rules'), api<StockSettings>('/stock/settings')])
      .then(([rules, settings]) => {
        const rule = rules.find((r) => r.channel === channel)
        if (rule) { setSafety(String(rule.safetyStock)); setPercent(String(rule.allocationPercent)); setSaved(rule.saved) }
        setDeduct(settings.deductAcrossChannels)
      })
      .catch((e: Error) => setMessage({ ok: false, text: e.message }))
  }, [channel])

  useEffect(() => {
    const t = setTimeout(() => { setTerm(search); setPage(1) }, 300)
    return () => clearTimeout(t)
  }, [search])

  useEffect(() => {
    let current = true
    const qs = new URLSearchParams({ channel, page: String(page), pageSize: String(PAGE_SIZE) })
    if (term.trim()) qs.set('q', term.trim())
    api<StockPage>(`/stock/products?${qs}`)
      .then((d) => { if (current) setData(d) })
      .catch((e: Error) => { if (current) setMessage({ ok: false, text: e.message }) })
    return () => { current = false }
  }, [channel, term, page, reload])

  const ruleSafety = Number(safety)
  const rulePercent = Number(percent.replace(',', '.'))
  const ruleError = safety.trim() === '' || percent.trim() === '' ? 'Güvenlik stoğu ve payı girin.' : validateStockRule(ruleSafety, rulePercent)

  async function saveRule() {
    setBusy(true)
    setMessage(undefined)
    try {
      await api(`/stock/rules/${encodeURIComponent(channel)}`, { method: 'PUT', body: JSON.stringify({ safetyStock: ruleSafety, allocationPercent: rulePercent }) })
      setSaved(true)
      setMessage({ ok: true, text: `${channel} stok kuralı kaydedildi.` })
      setReload((r) => r + 1)
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Kaydedilemedi.' })
    } finally {
      setBusy(false)
    }
  }

  async function toggleDeduct(next: boolean) {
    setDeduct(next)
    try {
      await api('/stock/settings', { method: 'PUT', body: JSON.stringify({ deductAcrossChannels: next }) })
    } catch (e) {
      setDeduct(!next)
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Ayar kaydedilemedi.' })
    }
  }

  const refresh = useCallback(() => setReload((r) => r + 1), [])

  const columns: Column<StockRow>[] = [
    { key: 'name', header: 'Ürün', render: (r) => <div className="stock-product"><strong>{r.name}</strong><span>{r.barcode}</span></div> },
    { key: 'stock', header: 'Fiziksel stok', render: (r) => r.stock },
    { key: 'safety', header: 'Güvenlik stoğu', render: (r) => <SafetyCell row={r} defaultSafety={ruleError ? r.effectiveSafety : ruleSafety} onSaved={refresh} /> },
    // Kural formundaki taslak değerler tabloda anında görünsün diye açılan miktar yerelde de hesaplanır.
    { key: 'qty', header: `${channel}'a açılan`, render: (r) => <strong>{ruleError ? r.channelQuantity : channelQuantity(r.stock, effectiveSafety(r.safetyStock, ruleSafety), rulePercent)}</strong> },
  ]

  return (
    <section className="ps-card stock" aria-labelledby="stock-title">
      <h2 id="stock-title">{channel} stok kuralı</h2>
      <p className="pricing__formula">Kanala açılan miktar = (fiziksel stok − güvenlik stoğu) × açılan pay, aşağı yuvarlanır.</p>

      <div className="pricing__grid">
        <TextInput label="Varsayılan güvenlik stoğu (adet)" type="number" min={0} step={1} value={safety} onChange={(e) => { setSafety(e.target.value); setMessage(undefined) }} />
        <TextInput label="Kanala açılan pay (%)" type="number" min={0} max={100} step="0.01" value={percent} onChange={(e) => { setPercent(e.target.value); setMessage(undefined) }} />
      </div>
      {ruleError && <p className="channel-form__error" role="alert">{ruleError}</p>}

      <label className="stock__toggle">
        <input type="checkbox" checked={deduct} onChange={(e) => toggleDeduct(e.target.checked)} />
        <span>
          <strong>Bir kanalda satış olunca diğer kanallarda da stok düşsün</strong>
          <small>Açıkken fiziksel stok ortak havuzdur: satış stoğu azaltır ve tüm kanalların açılan miktarı yeniden hesaplanıp güncellenir. Siparişler bu ayarı #28'de uygulayacak.</small>
        </span>
      </label>

      {message && <p className={message.ok ? 'channel-form__ok' : 'channel-form__error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      <div className="channel-form__actions">
        <Button variant="primary" onClick={saveRule} disabled={busy || !!ruleError}>{saved ? 'Stok kuralını güncelle' : 'Stok kuralını kaydet'}</Button>
      </div>

      <h3 className="stock__subtitle">Ürün bazında dağıtım</h3>
      <input className="ps-input stock__search" type="search" placeholder="Ürün adı veya barkod ara" aria-label="Ürün adı veya barkod ara" value={search} onChange={(e) => setSearch(e.target.value)} />
      <DataTable columns={columns} rows={data.items} rowKey={(r) => r.productId} pageSize={PAGE_SIZE}
        serverPaging={{ page: page - 1, total: data.total, onPageChange: (p) => setPage(p + 1) }}
        emptyTitle="Ürün bulunamadı" emptyText="Aramayı değiştirmeyi deneyin ya da önce ürün ekleyin." />
      <p className="channel-form__note">Güvenlik stoğu boş bırakılırsa kanalın varsayılanı kullanılır; 0 yazarsanız bu ürün için güvenlik stoğu uygulanmaz.</p>
    </section>
  )
}

/** Ürüne özel güvenlik stoğu: odaktan çıkınca ya da Enter ile kaydedilir; boş bırakmak varsayılana döndürür. */
function SafetyCell({ row, defaultSafety, onSaved }: { row: StockRow; defaultSafety: number; onSaved: () => void }) {
  const [value, setValue] = useState(row.safetyStock === null ? '' : String(row.safetyStock))
  const [state, setState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle')
  useEffect(() => { setValue(row.safetyStock === null ? '' : String(row.safetyStock)) }, [row.safetyStock])

  async function commit() {
    const next = value.trim() === '' ? null : Number(value)
    if (next === row.safetyStock) return
    if (next !== null && (!Number.isInteger(next) || next < 0)) { setState('error'); return }
    setState('saving')
    try {
      await api(`/stock/products/${row.productId}/safety`, { method: 'PUT', body: JSON.stringify({ safetyStock: next }) })
      setState('saved')
      onSaved()
    } catch {
      setState('error')
    }
  }

  return (
    <span className="stock__safety">
      <input className="ps-input" type="number" min={0} step={1} value={value} placeholder={`Varsayılan (${defaultSafety})`}
        aria-label={`${row.name} güvenlik stoğu`} aria-invalid={state === 'error'}
        onChange={(e) => { setValue(e.target.value); setState('idle') }}
        onBlur={commit} onKeyDown={(e) => { if (e.key === 'Enter') e.currentTarget.blur() }} />
      {state === 'saved' && <span role="status" className="stock__ok">✓</span>}
      {state === 'error' && <span role="alert" className="stock__bad">Geçersiz</span>}
    </span>
  )
}
