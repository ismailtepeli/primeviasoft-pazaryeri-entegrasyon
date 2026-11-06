import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import { Button, Select, StatusBadge, TextInput } from '../components'
import { timeAgo } from '../lib/timeAgo'
import {
  emptyForm, feedRoles, feedStatus, intervalLabel, intervalOptions, missingRoles, roleHint, roleLabel, runSummary, setRolePath, toForm, toggleImagePath, validateFeed,
  type FeedAnalysis, type FeedForm, type FeedMapping, type FeedRole, type FeedRunResult, type ProductFeed,
} from './feedView'
import './FeedImport.css'

const short = (s: string, n = 40) => (s.length > n ? `${s.slice(0, n)}…` : s)

/** Dropshipping: tedarikçinin XML ürün beslemesini adresten tanıyıp ürünleri ekler, stok ve fiyatı periyodik günceller. */
export default function FeedImport() {
  const [feeds, setFeeds] = useState<ProductFeed[]>()
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [editing, setEditing] = useState<{ id: string | null }>()
  const [form, setForm] = useState<FeedForm>(emptyForm())
  const [analysis, setAnalysis] = useState<FeedAnalysis>()
  const [analyzing, setAnalyzing] = useState(false)
  const [busy, setBusy] = useState<string>()
  const [result, setResult] = useState<{ name: string; run: FeedRunResult }>()
  const [confirmDelete, setConfirmDelete] = useState<string>()

  const load = useCallback(() => {
    api<ProductFeed[]>('/feeds').then(setFeeds).catch((e: Error) => setMessage({ ok: false, text: e.message }))
  }, [])
  useEffect(load, [load])

  // Çalışan bir besleme varsa durum birkaç saniyede bir yenilenir.
  useEffect(() => {
    if (!feeds?.some((f) => f.running)) return
    const t = window.setInterval(load, 4000)
    return () => window.clearInterval(t)
  }, [feeds, load])

  const set = <K extends keyof FeedForm>(key: K, value: FeedForm[K]) => setForm((f) => ({ ...f, [key]: value }))

  function startNew() { setEditing({ id: null }); setForm(emptyForm()); setAnalysis(undefined); setMessage(undefined); setResult(undefined) }
  function startEdit(f: ProductFeed) { setEditing({ id: f.id }); setForm(toForm(f)); setAnalysis(undefined); setMessage(undefined); setResult(undefined) }
  function cancel() { setEditing(undefined); setAnalysis(undefined); setMessage(undefined) }

  async function analyze(override?: { itemPath?: string; mapping?: FeedMapping }) {
    if (!editing) return
    if (editing.id === null && !form.url.trim()) { setMessage({ ok: false, text: 'Besleme adresini yazın.' }); return }
    setAnalyzing(true); setMessage(undefined)
    try {
      const body = JSON.stringify({
        url: form.url.trim() || null, itemPath: override?.itemPath ?? null, mapping: override?.mapping ?? null,
        priceIncludesVat: form.priceIncludesVat, defaultVatRate: Number(form.defaultVat) || 20, availableStock: Number(form.availableStock) || 0,
      })
      const a = await api<FeedAnalysis>(editing.id ? `/feeds/${editing.id}/analyze` : '/feeds/analyze', { method: 'POST', body })
      setAnalysis(a)
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
    finally { setAnalyzing(false) }
  }

  const changeMapping = (mapping: FeedMapping) => analysis && analyze({ itemPath: analysis.itemPath, mapping })

  async function save(runAfter: boolean) {
    if (!editing) return
    const current = analysis ? { itemPath: analysis.itemPath, mapping: analysis.mapping, missing: missingRoles(analysis.mapping) } : null
    const checked = validateFeed(form, current, editing.id !== null)
    if ('error' in checked) { setMessage({ ok: false, text: checked.error }); return }
    setBusy(runAfter ? 'run' : 'save'); setMessage(undefined)
    try {
      const saved = await api<ProductFeed>(editing.id ? `/feeds/${editing.id}` : '/feeds', { method: editing.id ? 'PUT' : 'POST', body: JSON.stringify(checked.body) })
      setEditing(undefined); setAnalysis(undefined)
      if (runAfter) {
        const run = await api<FeedRunResult>(`/feeds/${saved.id}/run`, { method: 'POST' })
        setResult({ name: saved.name, run })
      } else setMessage({ ok: true, text: 'Besleme kaydedildi.' })
      load()
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }); load() }
    finally { setBusy(undefined) }
  }

  async function runNow(f: ProductFeed) {
    setBusy(f.id); setMessage(undefined); setResult(undefined)
    try { setResult({ name: f.name, run: await api<FeedRunResult>(`/feeds/${f.id}/run`, { method: 'POST' }) }) }
    catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
    finally { setBusy(undefined); load() }
  }

  async function remove(id: string) {
    setBusy(id)
    try { await api(`/feeds/${id}`, { method: 'DELETE' }); setConfirmDelete(undefined); setMessage({ ok: true, text: 'Besleme silindi; eklediği ürünler katalogda kaldı.' }); load() }
    catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
    finally { setBusy(undefined) }
  }

  const missing = analysis ? missingRoles(analysis.mapping) : []

  return (
    <div className="feeds price-lists">
      <section className="ps-card price-editor" aria-labelledby="feed-title">
        <div className="price-editor__head">
          <h2 id="feed-title">XML beslemeleri</h2>
          {!editing && <Button variant="primary" onClick={startNew}>Besleme ekle</Button>}
        </div>
        <p className="price-cell__meta" style={{ margin: 0 }}>
          Tedarikçinin XML ürün adresini girin; biçim otomatik tanınır, ürünler barkoda göre eklenir. Stok ve fiyat seçtiğiniz aralıkta güncel tutulur,
          ad/açıklama/görsel yalnızca ilk eklemede yazılır (sonradan yaptığınız düzenleme ezilmez).
        </p>
        {message && <p className={message.ok ? 'price-ok' : 'price-error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
        {feeds === undefined && !message && <p className="price-cell__meta" aria-busy="true">Yükleniyor…</p>}
        {feeds?.length === 0 && !editing && <p className="price-cell__meta">Henüz besleme yok. "Besleme ekle" ile ilk tedarikçinizi bağlayın.</p>}

        {feeds && feeds.length > 0 && (
          <ul className="carrier-list">
            {feeds.map((f) => {
              const st = feedStatus(f)
              return (
                <li key={f.id} className="feed-row">
                  <div className="feed-row__top">
                    <div className="carrier-row__main">
                      <strong>{f.name}</strong> <StatusBadge variant={st.variant}>{st.text}</StatusBadge>
                      <span className="price-cell__meta">{f.productCount} ürün · {intervalLabel(f.intervalMinutes)}{f.lastRunAt ? ` · son çalışma ${timeAgo(f.lastRunAt)}` : ''}</span>
                    </div>
                    <div className="carrier-row__actions">
                      {confirmDelete === f.id ? (
                        <>
                          <span className="price-cell__meta">Silinsin mi? Eklediği {f.productCount} ürün katalogda kalır.</span>
                          <Button onClick={() => remove(f.id)} disabled={busy === f.id}>Evet, sil</Button>
                          <Button onClick={() => setConfirmDelete(undefined)}>Vazgeç</Button>
                        </>
                      ) : (
                        <>
                          <Button onClick={() => runNow(f)} disabled={busy !== undefined || f.running}>{busy === f.id ? 'Çalışıyor…' : 'Şimdi çalıştır'}</Button>
                          <Button onClick={() => startEdit(f)} aria-label={`${f.name} beslemesini düzenle`}>Düzenle</Button>
                          <Button onClick={() => setConfirmDelete(f.id)} aria-label={`${f.name} beslemesini sil`}>Sil</Button>
                        </>
                      )}
                    </div>
                  </div>
                  <p className="price-cell__meta feed-row__meta">{f.urlDisplay}{f.format ? ` · ${f.format}` : ''}</p>
                  {f.lastError && <p className="price-error feed-row__meta" role="alert">{f.lastError}</p>}
                  {f.lastStatus && !f.lastError && <p className="price-cell__meta feed-row__meta">Son çalışma: {runSummary({ created: f.lastCreated, updated: f.lastUpdated, skipped: f.lastSkipped, failed: f.lastFailed })}</p>}
                  {f.lastErrors.length > 0 && (
                    <details className="feed-row__meta">
                      <summary>Satır hataları ({f.lastErrors.length}{f.lastErrors.length >= 50 ? '+' : ''})</summary>
                      <ul className="price-errors">{f.lastErrors.slice(0, 10).map((e, i) => <li key={i}>{e.item}. kayıt{e.key ? ` (${short(e.key, 30)})` : ''}: {e.message}</li>)}</ul>
                    </details>
                  )}
                </li>
              )
            })}
          </ul>
        )}

        {result && (
          <div className="feed-result" role="status">
            <strong>{result.name}: {result.run.status === 'Success' ? 'Ürünler aktarıldı' : 'Aktarım tamamlandı, bazı kayıtlar atlandı'}</strong>
            <span>{runSummary(result.run)}</span>
            {result.run.errors.length > 0 && <ul className="price-errors">{result.run.errors.slice(0, 8).map((e, i) => <li key={i}>{e.item}. kayıt{e.key ? ` (${short(e.key, 30)})` : ''}: {e.message}</li>)}</ul>}
          </div>
        )}
      </section>

      {editing && (
        <section className="ps-card price-editor" aria-labelledby="feed-edit-title">
          <h2 id="feed-edit-title" style={{ margin: 0, fontSize: 18 }}>{editing.id ? 'Beslemeyi düzenle' : 'Yeni XML beslemesi'}</h2>
          <div className="price-form">
            <TextInput label="Besleme adı" value={form.name} maxLength={100} onChange={(e) => set('name', e.target.value)} hint="Örn. tedarikçi firmanın adı" />
            <TextInput label={editing.id ? 'Besleme adresi (değiştirmek için)' : 'Besleme adresi'} type="url" value={form.url} onChange={(e) => set('url', e.target.value)}
              placeholder={editing.id ? 'Boş bırakırsanız kayıtlı adres kullanılır' : 'https://tedarikci.com/xml/urunler.xml?anahtar=…'}
              hint="http/https; anahtar gerekiyorsa adresin sonunda verin" />
          </div>
          <div className="price-editor__actions" style={{ justifyContent: 'flex-start' }}>
            <Button onClick={() => analyze(analysis ? { itemPath: analysis.itemPath, mapping: analysis.mapping } : undefined)} disabled={analyzing}>
              {analyzing ? 'Adres okunuyor…' : analysis ? 'Yeniden çözümle' : editing.id ? 'Eşlemeyi göster ve düzenle' : 'Adresi çözümle'}
            </Button>
          </div>

          {analysis && (
            <div className="feed-analysis">
              <p className="price-ok" role="status">Biçim tanındı: <strong>{analysis.format}</strong> · {analysis.itemCount.toLocaleString('tr-TR')} ürün bulundu</p>
              {analysis.candidates.length > 1 && (
                <Select label="Ürün öğesi" hint="Yanlış satır seçildiyse (ör. varyant yerine ürün) değiştirin" value={analysis.itemPath}
                  onChange={(e) => analyze({ itemPath: e.target.value })}>
                  {analysis.candidates.map((c) => <option key={c.path} value={c.path}>{c.path} ({c.count.toLocaleString('tr-TR')} adet)</option>)}
                </Select>
              )}

              <fieldset className="price-pay">
                <legend>Alan eşleme</legend>
                <div className="feed-map">
                  {feedRoles.map((role: FeedRole) => {
                    const chosen = analysis.mapping[role] ?? []
                    const required = role === 'barcode' || role === 'name' || role === 'price'
                    const sample = (p: string) => analysis.fields.find((f) => f.path === p)?.samples[0]
                    if (role === 'images') {
                      return (
                        <div key={role} className="feed-map__row">
                          <div className="feed-map__label"><strong>{roleLabel[role]}</strong><span className="price-cell__meta">{roleHint[role]}</span></div>
                          <div className="feed-map__images">
                            {analysis.fields.filter((f) => f.samples.some((s) => /^(https?:)?\/\//i.test(s))).map((f) => (
                              <label key={f.path} className="price-cat-row">
                                <input type="checkbox" checked={chosen.includes(f.path)} onChange={(e) => changeMapping(toggleImagePath(analysis.mapping, f.path, e.target.checked))} /> {f.path}
                              </label>
                            ))}
                            {analysis.fields.every((f) => !f.samples.some((s) => /^(https?:)?\/\//i.test(s))) && <span className="price-cell__meta">Görsel adresi içeren alan bulunamadı</span>}
                          </div>
                        </div>
                      )
                    }
                    return (
                      <div key={role} className="feed-map__row">
                        <div className="feed-map__label"><strong>{roleLabel[role]}{required ? ' *' : ''}</strong>{roleHint[role] && <span className="price-cell__meta">{roleHint[role]}</span>}</div>
                        <Select label={`${roleLabel[role]} alanı`} value={chosen[0] ?? ''} onChange={(e) => changeMapping(setRolePath(analysis.mapping, role, e.target.value))} aria-label={`${roleLabel[role]} alanı`}>
                          <option value="">— eşleme yok —</option>
                          {analysis.fields.map((f) => <option key={f.path} value={f.path}>{f.path}{f.samples[0] ? ` — ${short(f.samples[0], 30)}` : ''}</option>)}
                        </Select>
                        {chosen[0] && sample(chosen[0]) && <span className="price-cell__meta feed-map__sample">Örnek: {short(sample(chosen[0])!, 50)}</span>}
                      </div>
                    )
                  })}
                </div>
                {missing.length > 0 && <p className="price-error" role="alert">Zorunlu alan eşlenmedi: {missing.map((r) => roleLabel[r]).join(', ')}.</p>}
              </fieldset>

              {analysis.preview.length > 0 && (
                <div className="feed-preview">
                  <h3>Önizleme (ilk {analysis.preview.length} ürün)</h3>
                  <div className="feed-table-wrap">
                    <table className="feed-table">
                      <thead><tr><th>Barkod</th><th>Ürün</th><th>Fiyat</th><th>Stok</th><th>Kategori</th><th>Görsel</th></tr></thead>
                      <tbody>
                        {analysis.preview.map((r, i) => (
                          <tr key={i} className={r.problems.length > 0 ? 'feed-table__bad' : undefined}>
                            <td>{r.barcode ?? '—'}</td>
                            <td>{r.name ? short(r.name, 50) : '—'}{r.problems.length > 0 && <span className="price-error"> {r.problems.join(' ')}</span>}</td>
                            <td>{r.price !== null ? r.price.toLocaleString('tr-TR', { minimumFractionDigits: 2 }) : '—'}</td>
                            <td>{r.stock ?? '—'}</td>
                            <td>{r.category ?? '—'}</td>
                            <td>{r.imageCount}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </div>
          )}

          <fieldset className="price-pay">
            <legend>Fiyat ve stok</legend>
            <div className="price-form">
              <TextInput label="Kâr yüzdesi (%)" hint="Satış fiyatı = tedarikçi fiyatı × (1 + kâr %). Tedarikçi fiyatı ürünün maliyetine yazılır." inputMode="decimal" value={form.markup} onChange={(e) => set('markup', e.target.value)} />
              <TextInput label="Varsayılan KDV (%)" hint="Beslemede KDV yoksa kullanılır" inputMode="numeric" value={form.defaultVat} onChange={(e) => set('defaultVat', e.target.value)} />
              <TextInput label='"Stokta var" adedi' hint="Beslemede yalnızca stokta var/yok yazıyorsa stokta olanlara bu adet yazılır" inputMode="numeric" value={form.availableStock} onChange={(e) => set('availableStock', e.target.value)} />
              <Select label="Güncelleme sıklığı" value={form.intervalMinutes} onChange={(e) => set('intervalMinutes', Number(e.target.value))}>
                {intervalOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
              </Select>
            </div>
            <label className="price-cat-row"><input type="checkbox" checked={form.priceIncludesVat} onChange={(e) => set('priceIncludesVat', e.target.checked)} /> Tedarikçi fiyatı KDV dahil (kapalıysa satış fiyatına KDV eklenir)</label>
            <label className="price-cat-row"><input type="checkbox" checked={form.syncPrice} onChange={(e) => set('syncPrice', e.target.checked)} /> Fiyatı her çalışmada güncelle</label>
            <label className="price-cat-row"><input type="checkbox" checked={form.syncStock} onChange={(e) => set('syncStock', e.target.checked)} /> Stoğu her çalışmada güncelle</label>
            <label className="price-cat-row"><input type="checkbox" checked={form.zeroMissing} onChange={(e) => set('zeroMissing', e.target.checked)} /> Beslemeden kalkan ürünün stoğunu sıfırla (ürün silinmez)</label>
          </fieldset>

          <fieldset className="price-pay">
            <legend>Yeni ürünler nerede görünsün</legend>
            <label className="price-cat-row"><input type="checkbox" checked={form.publishB2C} onChange={(e) => set('publishB2C', e.target.checked)} /> B2C mağazada göster</label>
            <label className="price-cat-row"><input type="checkbox" checked={form.publishB2B} onChange={(e) => set('publishB2B', e.target.checked)} /> B2B bayi sitesinde göster</label>
            <p className="price-cell__meta" style={{ margin: 0 }}>Kapalı bırakırsanız ürünler yalnızca katalogda olur; fiyat ve açıklamayı gözden geçirdikten sonra Ürünler ekranından açabilirsiniz.</p>
            <label className="price-cat-row"><input type="checkbox" checked={form.enabled} onChange={(e) => set('enabled', e.target.checked)} /> Besleme açık (kapalıyken otomatik çalışmaz)</label>
          </fieldset>

          {message && <p className={message.ok ? 'price-ok' : 'price-error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
          <div className="price-editor__actions">
            <Button onClick={cancel}>Vazgeç</Button>
            <Button onClick={() => save(false)} disabled={busy !== undefined}>{busy === 'save' ? 'Kaydediliyor…' : 'Kaydet'}</Button>
            <Button variant="primary" onClick={() => save(true)} disabled={busy !== undefined}>{busy === 'run' ? 'Ürünler aktarılıyor…' : 'Kaydet ve ürünleri aktar'}</Button>
          </div>
        </section>
      )}
    </div>
  )
}
