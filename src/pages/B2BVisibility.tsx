import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { B2BCategory, B2BRestriction, B2BSettings, DealerGroup, ProductPage, StockDisplayKind } from '../api/types'
import { Button, Select, TextInput } from '../components'
import { parseCount, stockDisplayOptions } from './b2bView'
import './PriceLists.css'

/** Ayarlar > B2B görünürlük: stok gösterimi, kategori gizleme ve bayi grubuna özel kısıtlar. Ürün bayrağı Ürünler ekranından açılır. */
export default function B2BVisibility() {
  const [settings, setSettings] = useState<B2BSettings>({ stockDisplay: 'Level', lowStockThreshold: 5 })
  const [threshold, setThreshold] = useState('5')
  const [categories, setCategories] = useState<B2BCategory[]>([])
  const [restrictions, setRestrictions] = useState<B2BRestriction[]>([])
  const [groups, setGroups] = useState<DealerGroup[]>([])
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()

  const [groupId, setGroupId] = useState('')
  const [kind, setKind] = useState<'category' | 'product'>('category')
  const [categoryId, setCategoryId] = useState('')
  const [productQuery, setProductQuery] = useState('')
  const [productResults, setProductResults] = useState<{ id: string; name: string }[]>([])
  const [productId, setProductId] = useState('')

  const fail = (e: unknown) => setMessage({ ok: false, text: (e as Error).message })

  const load = useCallback(() => {
    api<B2BSettings>('/b2b/settings').then((s) => { setSettings(s); setThreshold(String(s.lowStockThreshold)) }).catch(fail)
    api<B2BCategory[]>('/b2b/categories').then(setCategories).catch(fail)
    api<B2BRestriction[]>('/b2b/restrictions').then(setRestrictions).catch(fail)
    api<DealerGroup[]>('/dealer-groups').then(setGroups).catch(fail)
  }, [])
  useEffect(load, [load])

  useEffect(() => {
    if (kind !== 'product' || productQuery.trim().length < 2) { setProductResults([]); return }
    const t = setTimeout(() => {
      api<ProductPage>(`/products?q=${encodeURIComponent(productQuery.trim())}&pageSize=10`)
        .then((p) => setProductResults(p.items.map((x) => ({ id: x.id, name: `${x.name} (${x.barcode})` })))).catch(fail)
    }, 300)
    return () => clearTimeout(t)
  }, [kind, productQuery])

  async function saveSettings() {
    const n = parseCount(threshold)
    if (n == null || n > 100000) { setMessage({ ok: false, text: 'Az kaldı eşiği 0 ile 100000 arasında bir tam sayı olmalı.' }); return }
    try {
      await api('/b2b/settings', { method: 'PUT', body: JSON.stringify({ stockDisplay: settings.stockDisplay, lowStockThreshold: n }) })
      setMessage({ ok: true, text: 'Stok gösterimi kaydedildi.' })
    } catch (e) { fail(e) }
  }

  async function toggleCategory(c: B2BCategory) {
    try { await api(`/b2b/categories/${c.id}/hidden`, { method: 'PUT', body: JSON.stringify({ hidden: !c.hidden }) }); setMessage(undefined); load() }
    catch (e) { fail(e) }
  }

  async function addRestriction() {
    if (!groupId) { setMessage({ ok: false, text: 'Bir bayi grubu seçin.' }); return }
    const target = kind === 'category' ? categoryId : productId
    if (!target) { setMessage({ ok: false, text: kind === 'category' ? 'Bir kategori seçin.' : 'Bir ürün seçin.' }); return }
    try {
      await api('/b2b/restrictions', { method: 'POST', body: JSON.stringify({ groupId, categoryId: kind === 'category' ? target : null, productId: kind === 'product' ? target : null }) })
      setMessage({ ok: true, text: 'Kısıt eklendi.' }); setCategoryId(''); setProductId(''); load()
    } catch (e) { fail(e) }
  }

  async function removeRestriction(r: B2BRestriction) {
    try { await api(`/b2b/restrictions/${r.id}`, { method: 'DELETE' }); setMessage(undefined); load() }
    catch (e) { fail(e) }
  }

  return (
    <div className="price-lists">
      {message && <p className={message.ok ? 'price-ok' : 'price-error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}

      <section className="ps-card price-editor" aria-labelledby="b2b-stock-title">
        <h2 id="b2b-stock-title" style={{ margin: 0, fontSize: 18 }}>Stok gösterimi</h2>
        <p className="price-cell__meta" style={{ margin: 0 }}>Bayi sitesinde stok, fiziksel stoktan ürünün güvenlik stoğu düşülerek gösterilir.</p>
        <div className="price-form">
          <Select label="Gösterim" value={settings.stockDisplay} onChange={(e) => setSettings({ ...settings, stockDisplay: e.target.value as StockDisplayKind })}>
            {stockDisplayOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </Select>
          <TextInput label="Az kaldı eşiği" hint="Bu adet ve altı 'Az kaldı' görünür" inputMode="numeric" value={threshold} onChange={(e) => setThreshold(e.target.value)} />
        </div>
        <div className="price-editor__actions"><Button onClick={saveSettings}>Kaydet</Button></div>
      </section>

      <section className="ps-card price-editor" aria-labelledby="b2b-cat-title">
        <h2 id="b2b-cat-title" style={{ margin: 0, fontSize: 18 }}>Kategori görünürlüğü</h2>
        <p className="price-cell__meta" style={{ margin: 0 }}>Gizlenen kategori, alt kategorileri ve ürünleriyle bayi sitesinde görünmez.</p>
        {categories.length === 0 ? <p className="price-cell__meta">Henüz kategori yok.</p> : (
          <ul className="price-errors" style={{ listStyle: 'none', paddingLeft: 0, color: 'inherit' }}>
            {categories.map((c) => (
              <li key={c.id}>
                <label className="price-cat-row">
                  <input type="checkbox" checked={c.hidden} onChange={() => toggleCategory(c)} aria-label={`${c.path} kategorisini bayi sitesinde gizle`} />
                  {c.path} <span className="price-cell__meta">· {c.productCount} ürün{c.hidden ? ' · gizli' : ''}</span>
                </label>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="ps-card price-editor" aria-labelledby="b2b-restr-title">
        <h2 id="b2b-restr-title" style={{ margin: 0, fontSize: 18 }}>Bayi grubuna özel kısıtlar</h2>
        <p className="price-cell__meta" style={{ margin: 0 }}>Seçilen grubun bayileri bu ürün ya da kategoriyi (alt kategorileriyle) görmez.</p>
        <div className="price-form">
          <Select label="Bayi grubu" value={groupId} onChange={(e) => setGroupId(e.target.value)}>
            <option value="">Seçin</option>
            {groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
          </Select>
          <Select label="Gizlenecek" value={kind} onChange={(e) => setKind(e.target.value as 'category' | 'product')}>
            <option value="category">Kategori</option>
            <option value="product">Ürün</option>
          </Select>
          {kind === 'category' ? (
            <Select label="Kategori" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
              <option value="">Seçin</option>
              {categories.map((c) => <option key={c.id} value={c.id}>{c.path}</option>)}
            </Select>
          ) : (
            <>
              <TextInput label="Ürün ara" hint="En az 2 karakter: ad ya da barkod" value={productQuery} onChange={(e) => setProductQuery(e.target.value)} />
              <Select label="Ürün" value={productId} onChange={(e) => setProductId(e.target.value)}>
                <option value="">Seçin</option>
                {productResults.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </Select>
            </>
          )}
        </div>
        <div className="price-editor__actions"><Button onClick={addRestriction}>Kısıt ekle</Button></div>
        {restrictions.length > 0 && (
          <ul className="price-errors" style={{ listStyle: 'none', paddingLeft: 0, color: 'inherit' }}>
            {restrictions.map((r) => (
              <li key={r.id} className="price-check-row">
                <span><strong>{r.groupName}</strong> grubu: {r.productName ?? r.categoryPath}{r.productId ? ' (ürün)' : ' (kategori)'}</span>
                <Button onClick={() => removeRestriction(r)}>Kaldır</Button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}
