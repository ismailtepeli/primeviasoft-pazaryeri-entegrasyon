import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { B2BCategory } from '../api/types'
import B2CCarrierSettings from './B2CCarrierSettings'
import B2CCheckoutSettings from './B2CCheckoutSettings'
import './PriceLists.css'

/** Ayarlar > B2C mağaza: kargo ve ödeme ayarları ile kategori gizleme. Ürün bayrağı ve kampanya fiyatı Ürünler ekranından yönetilir. */
export default function B2CVisibility() {
  const [categories, setCategories] = useState<B2BCategory[]>([])
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const fail = (e: unknown) => setMessage({ ok: false, text: (e as Error).message })

  const load = useCallback(() => { api<B2BCategory[]>('/b2c/categories').then(setCategories).catch(fail) }, [])
  useEffect(load, [load])

  async function toggle(c: B2BCategory) {
    try { await api(`/b2c/categories/${c.id}/hidden`, { method: 'PUT', body: JSON.stringify({ hidden: !c.hidden }) }); setMessage(undefined); load() }
    catch (e) { fail(e) }
  }

  return (
    <div className="price-lists">
      {message && <p className={message.ok ? 'price-ok' : 'price-error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      <B2CCheckoutSettings />
      <B2CCarrierSettings />
      <section className="ps-card price-editor" aria-labelledby="b2c-cat-title">
        <h2 id="b2c-cat-title" style={{ margin: 0, fontSize: 18 }}>Kategori görünürlüğü</h2>
        <p className="price-cell__meta" style={{ margin: 0 }}>Gizlenen kategori, alt kategorileri ve ürünleriyle B2C mağazada görünmez. B2B bayi sitesi bu ayardan etkilenmez. Ürün sayısı, B2C mağazada görünür olarak işaretli ürünlerdir.</p>
        {categories.length === 0 ? <p className="price-cell__meta">Henüz kategori yok.</p> : (
          <ul className="price-errors" style={{ listStyle: 'none', paddingLeft: 0, color: 'inherit' }}>
            {categories.map((c) => (
              <li key={c.id}>
                <label className="price-cat-row">
                  <input type="checkbox" checked={c.hidden} onChange={() => toggle(c)} aria-label={`${c.path} kategorisini B2C mağazada gizle`} />
                  {c.path} <span className="price-cell__meta">· {c.productCount} ürün{c.hidden ? ' · gizli' : ''}</span>
                </label>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}
