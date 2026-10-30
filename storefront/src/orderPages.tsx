import { useEffect, useState } from 'react'
import { Truck } from 'lucide-react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { StoreApiError, storeApi } from './api'
import { formatMoney } from './catalog'
import { useCart } from './CartContext'
import { formatDate, orderStatusText, orderSteps, priceChangeNote, type OrderDetail, type OrderRow } from './orders'
import { useStore } from './StoreContext'

interface IssueBody { details?: { issues?: { kind: string; text: string }[] } }

export function CartPage() {
  const { token } = useStore()
  const { cart, setQuantity, refresh, replace } = useCart()
  const navigate = useNavigate()
  const [qty, setQty] = useState<Record<string, string>>({})
  const [address, setAddress] = useState('')
  const [note, setNote] = useState('')
  const [partial, setPartial] = useState(false)
  const [message, setMessage] = useState<string>()
  const [issues, setIssues] = useState<string[]>([])
  const [busy, setBusy] = useState(false)

  useEffect(() => { void refresh() }, [refresh])

  async function change(productId: string, value: number) {
    setMessage(undefined)
    try { await setQuantity(productId, value) } catch (e) { setMessage((e as Error).message) }
  }

  async function clear() {
    try { replace(await storeApi('/cart', { method: 'DELETE' }, token)) } catch (e) { setMessage((e as Error).message) }
  }

  async function checkout() {
    setBusy(true); setMessage(undefined); setIssues([])
    try {
      const order = await storeApi<OrderDetail>('/orders', { method: 'POST', body: JSON.stringify({ address, note, allowPartial: partial }) }, token)
      await refresh()
      navigate(`/siparis/${order.id}`, { state: { placed: true } })
    } catch (e) {
      setMessage((e as Error).message)
      if (e instanceof StoreApiError && e.status === 409) await refresh()
      const body = (e as { body?: IssueBody }).body
      setIssues(body?.details?.issues?.map((i) => i.text) ?? [])
    } finally { setBusy(false) }
  }

  if (cart.lines.length === 0) {
    return (
      <section className="ps-card store-panel">
        <h1>Sepet</h1>
        <p className="store-note">Sepetiniz boş.</p>
        <Link to="/" className="ps-btn">Ürünlere git</Link>
      </section>
    )
  }

  return (
    <div className="store-catalog">
      <h1>Sepet <span className="store-note">({cart.itemCount} adet)</span></h1>
      <div className="store-tablewrap">
        <table className="store-table">
          <thead><tr><th>Ürün</th><th>Birim fiyat</th><th>Adet</th><th>Tutar</th><th /></tr></thead>
          <tbody>
            {cart.lines.map((l) => {
              const note = priceChangeNote(l)
              return (
                <tr key={l.productId}>
                  <td>
                    <div className="store-cell">
                      {l.coverUrl ? <img className="store-thumb" src={l.coverUrl} alt={l.name} /> : <span className="store-thumb store-thumb--empty">—</span>}
                      <div>
                        {l.issue?.kind === 'unavailable' ? <strong>{l.name}</strong> : <Link to={`/urun/${l.productId}`}><strong>{l.name}</strong></Link>}
                        <div className="store-note">{l.code}</div>
                        {l.issue && <div role="alert" className="store-error">{l.issue.text}</div>}
                        {note && <div className="store-warn">{note}: eklediğinizde {formatMoney(l.priceAtAdd!, l.price.currency)} idi.</div>}
                      </div>
                    </div>
                  </td>
                  <td>{l.issue?.kind === 'unavailable' ? '—' : <>{formatMoney(l.price.amount, l.price.currency)}<div className="store-note">{l.price.includesVat ? 'KDV dahil' : 'KDV hariç'}</div></>}</td>
                  <td>
                    <input className="ps-input store-qty__input" inputMode="numeric" aria-label={`${l.name} adet`} value={qty[l.productId] ?? String(l.quantity)}
                      onChange={(e) => setQty((q) => ({ ...q, [l.productId]: e.target.value }))}
                      onBlur={() => { const v = Number(qty[l.productId]); if (qty[l.productId] !== undefined && Number.isInteger(v) && v !== l.quantity) void change(l.productId, v); setQty(({ [l.productId]: _, ...rest }) => rest) }} />
                  </td>
                  <td><strong>{l.issue?.kind === 'unavailable' ? '—' : formatMoney(l.lineTotal, l.price.currency)}</strong></td>
                  <td><button type="button" className="store-link-btn" onClick={() => change(l.productId, 0)} aria-label={`${l.name} sepetten çıkar`}>Çıkar</button></td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>

      <div className="store-summary ps-card">
        <div className="store-summary__row"><span>Ara toplam (KDV hariç)</span><strong>{formatMoney(cart.totalNet, cart.currency)}</strong></div>
        <div className="store-summary__row"><span>KDV</span><strong>{formatMoney(Math.round((cart.totalGross - cart.totalNet) * 100) / 100, cart.currency)}</strong></div>
        <div className="store-summary__row store-summary__row--total"><span>Genel toplam</span><strong>{formatMoney(cart.totalGross, cart.currency)}</strong></div>
      </div>

      <section className="ps-card store-panel" style={{ maxWidth: 'none', margin: 0 }} aria-labelledby="checkout-title">
        <h2 id="checkout-title" className="store-h2">Teslimat</h2>
        <label className="field">
          <span className="field__label">Teslimat adresi</span>
          <input className="ps-input" value={address} onChange={(e) => setAddress(e.target.value)} placeholder="Boş bırakırsanız kayıtlı adresiniz kullanılır" />
        </label>
        <label className="field">
          <span className="field__label">Sipariş notu</span>
          <input className="ps-input" value={note} onChange={(e) => setNote(e.target.value)} />
        </label>
        {cart.hasIssues && (
          <label className="store-check"><input type="checkbox" checked={partial} onChange={(e) => setPartial(e.target.checked)} /> Sorunlu ürünleri çıkarıp, karşılanabilen kısmı sipariş et</label>
        )}
        {message && <p role="alert" className="store-error">{message}</p>}
        {issues.length > 0 && <ul className="store-error">{issues.map((t) => <li key={t}>{t}</li>)}</ul>}
        <div className="store-actions">
          <button type="button" className="ps-btn ps-btn--primary" disabled={busy} onClick={checkout}>{busy ? 'Sipariş veriliyor…' : 'Siparişi ver'}</button>
          <button type="button" className="ps-btn" onClick={clear}>Sepeti boşalt</button>
        </div>
      </section>
    </div>
  )
}

export function OrdersPage() {
  const { token } = useStore()
  const [rows, setRows] = useState<OrderRow[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()

  useEffect(() => {
    let current = true
    setLoading(true)
    storeApi<{ items: OrderRow[]; total: number }>(`/orders?page=${page}`, {}, token)
      .then((d) => { if (current) { setRows(d.items); setTotal(d.total); setError(undefined) } })
      .catch((e: Error) => { if (current) setError(e.message) })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [page, token])

  const pages = Math.max(1, Math.ceil(total / 20))
  return (
    <div className="store-catalog">
      <h1>Siparişlerim</h1>
      {error && <p role="alert" className="store-error">{error}</p>}
      {!loading && rows.length === 0 && !error && <p className="store-note">Henüz sipariş vermediniz. <Link to="/">Ürünlere göz atın</Link>.</p>}
      {rows.length > 0 && (
        <div className="store-tablewrap">
          <table className="store-table">
            <thead><tr><th>Sipariş</th><th>Tarih</th><th>Durum</th><th>Tutar</th></tr></thead>
            <tbody>
              {rows.map((o) => (
                <tr key={o.id}>
                  <td><Link to={`/siparis/${o.id}`}><strong>{o.orderNumber}</strong></Link><div className="store-note">{o.lineCount} kalem</div></td>
                  <td>{formatDate(o.orderDate)}</td>
                  <td><span className="store-stock store-stock--low">{orderStatusText(o.status)}</span></td>
                  <td><strong>{formatMoney(o.totalGross, o.currency)}</strong></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {pages > 1 && (
        <nav className="store-pager" aria-label="Sayfalar">
          <button type="button" className="ps-btn" disabled={page <= 1} onClick={() => setPage(page - 1)}>Önceki</button>
          <span>Sayfa {page} / {pages}</span>
          <button type="button" className="ps-btn" disabled={page >= pages} onClick={() => setPage(page + 1)}>Sonraki</button>
        </nav>
      )}
    </div>
  )
}

export function OrderDetailPage() {
  const { id } = useParams()
  const { token } = useStore()
  const { refresh } = useCart()
  const navigate = useNavigate()
  const [order, setOrder] = useState<OrderDetail>()
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    let current = true
    storeApi<OrderDetail>(`/orders/${id}`, {}, token)
      .then((o) => { if (current) setOrder(o) })
      .catch((e: Error) => { if (current) setError(e.message) })
    return () => { current = false }
  }, [id, token])

  async function cancel() {
    try { setOrder(await storeApi<OrderDetail>(`/orders/${id}/cancel`, { method: 'POST' }, token)); setMessage({ ok: true, text: 'Sipariş iptal edildi.' }) }
    catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
  }

  async function reorder() {
    try {
      const r = await storeApi<{ added: number; skipped: string[] }>(`/orders/${id}/reorder`, { method: 'POST' }, token)
      await refresh()
      if (r.skipped.length === 0) navigate('/sepet')
      else setMessage({ ok: r.added > 0, text: `${r.added} ürün sepete eklendi. Eklenemeyenler: ${r.skipped.join(' ')}` })
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
  }

  if (error) return <div className="store-catalog"><p role="alert" className="store-error">{error}</p><Link to="/siparislerim">Siparişlerime dön</Link></div>
  if (!order) return <p className="store-note">Yükleniyor…</p>

  return (
    <div className="store-catalog">
      <nav className="store-crumbs" aria-label="Yol"><Link to="/siparislerim">Siparişlerim</Link> / <strong>{order.orderNumber}</strong></nav>
      <h1>{order.orderNumber} <span className="store-note">{formatDate(order.orderDate)}</span></h1>
      <ol className="store-steps" aria-label="Sipariş durumu">
        {orderSteps(order.status).map((s) => <li key={s.label} className={`store-steps__item store-steps__item--${s.state}`} aria-current={s.state === 'current' ? 'step' : undefined}>{s.label}</li>)}
      </ol>
      {message && <p role={message.ok ? 'status' : 'alert'} className={message.ok ? 'store-ok' : 'store-error'}>{message.text}</p>}
      <div className="store-tablewrap">
        <table className="store-table">
          <thead><tr><th>Ürün</th><th>Birim fiyat</th><th>Adet</th></tr></thead>
          <tbody>
            {order.lines.map((l) => (
              <tr key={`${l.code}-${l.name}`}>
                <td><strong>{l.name}</strong><div className="store-note">{l.code}</div></td>
                <td>{formatMoney(l.unitPrice, order.currency)}<div className="store-note">{l.priceIncludesVat ? 'KDV dahil' : 'KDV hariç'}</div></td>
                <td>{l.quantity}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="store-summary ps-card">
        <div className="store-summary__row"><span>Ara toplam (KDV hariç)</span><strong>{formatMoney(order.totalNet, order.currency)}</strong></div>
        <div className="store-summary__row store-summary__row--total"><span>Genel toplam (KDV dahil)</span><strong>{formatMoney(order.totalGross, order.currency)}</strong></div>
      </div>
      {order.shipmentCarrier && <p className="store-note"><Truck size={16} aria-hidden /> <strong>Sevk:</strong> {order.shipmentCarrier} · takip no <strong>{order.trackingNumber}</strong>{order.shippedAt ? ` · ${formatDate(order.shippedAt)}` : ''}</p>}
      {order.statusNote && <p role="status" className="store-warn"><strong>{order.status === 'Cancelled' ? 'İptal/ret gerekçesi:' : 'Not:'}</strong> {order.statusNote}</p>}
      {order.shippingAddress && <p className="store-note"><strong>Teslimat adresi:</strong> {order.shippingAddress}</p>}
      {order.note && <p className="store-note"><strong>Not:</strong> {order.note}</p>}
      <div className="store-actions">
        <button type="button" className="ps-btn" onClick={reorder}>Tekrar sipariş ver</button>
        {order.canCancel && <button type="button" className="ps-btn" onClick={cancel}>Siparişi iptal et</button>}
      </div>
    </div>
  )
}
