import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom'
import { ChevronRight, CircleCheck, Minus, Package, Plus, ShoppingCart, Trash2, TriangleAlert } from 'lucide-react'
import { ShopApiError, shopApi } from './api'
import { addressSummary, formatDateTime, type Address, type OrderDetail } from './account'
import { fixedQuantity, hasCarrierChoice, totalWith, type CartQuote, type CheckoutResult, type QuoteLine } from './cart'
import { useCart } from './CartContext'
import { useCustomer } from './CustomerContext'
import { productPath, usePageMeta } from './seo'
import { formatMoney } from './shop'
import { useShop } from './ShopContext'

/** Ürün kartı ve detayındaki "Sepete ekle" düğmesi; eklendiğini ekran okuyucuya da bildirir. */
export function AddToCartButton({ productId, disabled, primary = false }: { productId: string; disabled?: boolean; primary?: boolean }) {
  const { add } = useCart()
  const [added, setAdded] = useState(false)
  useEffect(() => {
    if (!added) return
    const t = window.setTimeout(() => setAdded(false), 2000)
    return () => window.clearTimeout(t)
  }, [added])
  return (
    <>
      <button type="button" className={`ps-btn${primary ? ' ps-btn--primary' : ''}`} disabled={disabled} onClick={() => { add(productId); setAdded(true) }}>
        {added ? <CircleCheck size={16} aria-hidden /> : <ShoppingCart size={16} aria-hidden />} {disabled ? 'Tükendi' : added ? 'Sepete eklendi' : 'Sepete ekle'}
      </button>
      <span className="sr-only" role="status">{added ? 'Ürün sepete eklendi' : ''}</span>
    </>
  )
}

/** Sepeti sunucuda fiyatlar; satırlar değişince (kısa gecikmeyle) yeniden sorar. */
function useQuote(carrierId: string | null = null) {
  const { tenant } = useShop()
  const { lines } = useCart()
  const [quote, setQuote] = useState<CartQuote | null>(null)
  const [error, setError] = useState<string>()
  const [loading, setLoading] = useState(lines.length > 0)
  const key = JSON.stringify([lines, carrierId])

  const refresh = useCallback(() => {
    if (lines.length === 0) { setQuote(null); setLoading(false); return () => {} }
    let current = true
    setLoading(true)
    const t = window.setTimeout(() => {
      shopApi<CartQuote>(tenant.slug, '/cart/quote', { method: 'POST', body: JSON.stringify({ lines, carrierId }) })
        .then((q) => { if (current) { setQuote(q); setError(undefined) } })
        .catch((e: Error) => { if (current) setError(e.message) })
        .finally(() => { if (current) setLoading(false) })
    }, 200)
    return () => { current = false; window.clearTimeout(t) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenant.slug, key])

  useEffect(() => refresh(), [refresh])
  return { quote, error, loading }
}

function EmptyCart() {
  return (
    <div className="store-empty">
      <ShoppingCart size={40} aria-hidden />
      <h1>Sepetiniz boş</h1>
      <p className="store-note">Beğendiğiniz ürünleri sepete ekleyin.</p>
      <Link to="/" className="ps-btn">Alışverişe başla</Link>
    </div>
  )
}

function Summary({ quote, method }: { quote: CartQuote; method?: string | null }) {
  const fee = quote.paymentOptions.find((o) => o.method === method)?.fee ?? 0
  return (
    <dl className="cart-summary">
      <div><dt>Ara toplam</dt><dd>{formatMoney(quote.subtotal, quote.currency)}</dd></div>
      <div><dt>Kargo{quote.carrierId && hasCarrierChoice(quote) ? ` (${quote.shippingOptions.find((o) => o.carrierId === quote.carrierId)?.name})` : ''}</dt><dd>{quote.shippingFee === 0 ? 'Ücretsiz' : formatMoney(quote.shippingFee, quote.currency)}</dd></div>
      {fee > 0 && <div><dt>Kapıda ödeme ücreti</dt><dd>{formatMoney(fee, quote.currency)}</dd></div>}
      {quote.freeShippingRemaining !== null && quote.freeShippingRemaining > 0 && (
        <p className="store-note">{formatMoney(quote.freeShippingRemaining, quote.currency)} daha ekleyin, kargo ücretsiz olsun.</p>
      )}
      <div className="cart-summary__total"><dt>Genel toplam <span className="store-note">(KDV dahil)</span></dt><dd>{formatMoney(totalWith(quote, method ?? null), quote.currency)}</dd></div>
    </dl>
  )
}

function CartRow({ line }: { line: QuoteLine }) {
  const { setQty, remove } = useCart()
  const fix = fixedQuantity(line)
  return (
    <li className={`cart-line${line.issue ? ' cart-line--issue' : ''}`}>
      {line.coverUrl ? <img className="order-row__img" src={line.coverUrl} alt="" loading="lazy" /> : <span className="order-row__img order-row__img--empty"><Package size={20} aria-hidden /></span>}
      <div className="cart-line__main">
        {line.issue?.kind === 'unavailable' ? <strong>{line.name}</strong> : <Link to={productPath(line.productId, line.name)}>{line.name}</Link>}
        {!line.issue && <span className="store-note">{formatMoney(line.unitPrice)}{line.listPrice ? <> · <s>{formatMoney(line.listPrice)}</s></> : null}</span>}
        {line.issue && (
          <span className="cart-line__issue" role="alert"><TriangleAlert size={14} aria-hidden /> {line.issue.text}
            {fix !== null && fix !== line.quantity && <button type="button" className="link-btn" onClick={() => setQty(line.productId, fix)}>Adedi {fix} yap</button>}
          </span>
        )}
      </div>
      {line.issue?.kind !== 'unavailable' && (
        <div className="cart-qty" role="group" aria-label={`${line.name} adedi`}>
          <button type="button" className="ps-btn cart-qty__btn" aria-label="Azalt" onClick={() => setQty(line.productId, line.quantity - 1)}><Minus size={14} aria-hidden /></button>
          <span className="cart-qty__n" aria-live="polite">{line.quantity}</span>
          <button type="button" className="ps-btn cart-qty__btn" aria-label="Artır" onClick={() => setQty(line.productId, line.quantity + 1)}><Plus size={14} aria-hidden /></button>
        </div>
      )}
      <strong className="cart-line__total">{line.issue ? '' : formatMoney(line.lineTotal)}</strong>
      <button type="button" className="icon-btn" aria-label={`${line.name} sepetten çıkar`} onClick={() => remove(line.productId)}><Trash2 size={16} aria-hidden /></button>
    </li>
  )
}

export function CartPage() {
  const { info } = useShop()
  const { lines } = useCart()
  const { quote, error, loading } = useQuote()
  usePageMeta({ title: `Sepetim | ${info.name}`, description: 'Sepetinizdeki ürünler.', noindex: true })
  if (lines.length === 0) return <EmptyCart />

  return (
    <div className="cart">
      <h1 className="account-title">Sepetim</h1>
      {error && <p role="alert" className="store-error">{error}</p>}
      {!quote && loading && <p className="store-note" aria-busy="true">Fiyatlar güncelleniyor…</p>}
      {quote && (
        <div className="cart-layout" aria-busy={loading}>
          <ul className="ps-card store-panel cart-lines">{quote.lines.map((l) => <CartRow key={l.productId} line={l} />)}</ul>
          <aside className="ps-card store-panel cart-side">
            <h2>Sipariş özeti</h2>
            <Summary quote={quote} />
            {quote.hasIssues && <p className="store-note" role="status">Stokta olmayan ürünleri düzeltin ya da sepetten çıkarın.</p>}
            <Link to="/odeme" className={`ps-btn ps-btn--primary${quote.hasIssues || loading ? ' is-disabled' : ''}`} aria-disabled={quote.hasIssues || loading} onClick={(e) => { if (quote.hasIssues || loading) e.preventDefault() }}>Siparişi tamamla</Link>
            <Link to="/" className="store-note">Alışverişe devam et</Link>
          </aside>
        </div>
      )}
    </div>
  )
}

export function CheckoutPage() {
  const { info } = useShop()
  const { lines, clear } = useCart()
  const { api } = useCustomer()
  const navigate = useNavigate()
  const [carrierId, setCarrierId] = useState<string | null>(null)
  const { quote, error: quoteError, loading } = useQuote(carrierId)
  const [addresses, setAddresses] = useState<Address[] | null>(null)
  const [addressId, setAddressId] = useState<string>('')
  const [method, setMethod] = useState<string>('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const [placed, setPlaced] = useState(false)
  usePageMeta({ title: `Ödeme | ${info.name}`, description: 'Siparişinizi tamamlayın.', noindex: true })

  useEffect(() => {
    let current = true
    api<Address[]>('/me/addresses').then((list) => {
      if (!current) return
      setAddresses(list)
      setAddressId((id) => id || (list.find((a) => a.isDefault) ?? list[0])?.id || '')
    }).catch((e: Error) => { if (current) setError(e.message) })
    return () => { current = false }
  }, [api])

  useEffect(() => { if (quote && !quote.paymentOptions.some((o) => o.method === method)) setMethod(quote.paymentOptions[0]?.method ?? '') }, [quote, method])
  // Seçili firma sepete uymuyorsa (ya da henüz seçilmediyse) sunucunun önerdiği ilk uygun firmaya geçilir; eski teklif seçimi ezmesin diye yalnızca seçeneklerde yoksa.
  useEffect(() => { if (quote && !quote.shippingOptions.some((o) => o.carrierId === carrierId)) setCarrierId(quote.carrierId) }, [quote, carrierId])

  // Sipariş verildikten sonra sepet boşalır; o durumda sepet sayfasına değil, sipariş sonucuna gidilir.
  if (lines.length === 0 && !placed) return <Navigate to="/sepet" replace />

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!addressId) { setError('Teslimat adresi seçin.'); return }
    if (!method) { setError('Ödeme yöntemi seçin.'); return }
    setBusy(true); setError(undefined)
    try {
      const result = await api<CheckoutResult>('/checkout', { method: 'POST', body: JSON.stringify({ lines, addressId, paymentMethod: method, note: note.trim() || null, carrierId }) })
      setPlaced(true)
      navigate(result.paymentUrl ?? `/siparis-tamam/${result.orderId}`, { replace: true })
      clear()
    } catch (err) {
      setError(err instanceof ShopApiError && err.status === 409 ? `${err.message} Sepetinizi kontrol edin.` : (err as Error).message)
    } finally { setBusy(false) }
  }

  const ready = quote && addresses !== null
  return (
    <div className="cart">
      <nav className="store-crumbs" aria-label="Yol"><Link to="/sepet">Sepetim</Link><span><ChevronRight size={14} aria-hidden /> <strong>Ödeme</strong></span></nav>
      <h1 className="account-title">Siparişi tamamla</h1>
      {quoteError && <p role="alert" className="store-error">{quoteError}</p>}
      {!ready && (loading || addresses === null) && <p className="store-note" aria-busy="true">Yükleniyor…</p>}
      {ready && quote.shippingOptions.length === 0 && <p role="alert" className="store-error">Bu sepet için uygun kargo seçeneği yok. Lütfen mağaza ile iletişime geçin.</p>}
      {ready && quote.hasIssues && <p role="alert" className="store-error">Sepetinizde stokta olmayan ürünler var. <Link to="/sepet">Sepete dönün</Link>.</p>}
      {ready && (
        <form className="cart-layout" onSubmit={submit} noValidate>
          <div className="account-stack">
            <section className="ps-card store-panel account-card" aria-labelledby="adres-baslik">
              <div className="account-head"><h2 id="adres-baslik">Teslimat adresi</h2><Link to="/hesap/adresler" className="ps-btn">Adresleri yönet</Link></div>
              {addresses.length === 0 ? (
                <p className="store-note">Kayıtlı adresiniz yok. <Link to="/hesap/adresler">Adres ekleyin</Link>, sonra buraya dönün.</p>
              ) : (
                <ul className="choice-list">
                  {addresses.map((a) => (
                    <li key={a.id}><label className={`choice${addressId === a.id ? ' choice--on' : ''}`}>
                      <input type="radio" name="address" checked={addressId === a.id} onChange={() => setAddressId(a.id)} />
                      <span><strong>{a.title}</strong> · {a.fullName}<br /><span className="store-note">{addressSummary(a)}</span></span>
                    </label></li>
                  ))}
                </ul>
              )}
            </section>

            {hasCarrierChoice(quote) && (
              <section className="ps-card store-panel account-card" aria-labelledby="kargo-baslik">
                <h2 id="kargo-baslik">Kargo firması</h2>
                <ul className="choice-list">
                  {quote.shippingOptions.map((o) => (
                    <li key={o.carrierId}><label className={`choice${carrierId === o.carrierId ? ' choice--on' : ''}`}>
                      <input type="radio" name="carrier" checked={carrierId === o.carrierId} onChange={() => setCarrierId(o.carrierId)} />
                      <span><strong>{o.name}</strong> · {o.fee === 0 ? 'Ücretsiz' : formatMoney(o.fee)}</span>
                    </label></li>
                  ))}
                </ul>
              </section>
            )}

            <section className="ps-card store-panel account-card" aria-labelledby="odeme-baslik">
              <h2 id="odeme-baslik">Ödeme yöntemi</h2>
              {quote.paymentOptions.length === 0 ? <p className="store-note">Bu mağazada şu an ödeme alınamıyor. Lütfen daha sonra tekrar deneyin.</p> : (
                <ul className="choice-list">
                  {quote.paymentOptions.map((o) => (
                    <li key={o.method}><label className={`choice${method === o.method ? ' choice--on' : ''}`}>
                      <input type="radio" name="payment" checked={method === o.method} onChange={() => setMethod(o.method)} />
                      <span><strong>{o.label}</strong>{o.fee > 0 && <span className="store-note"> · +{formatMoney(o.fee)}</span>}
                        {o.method === 'BankTransfer' && <><br /><span className="store-note">Siparişten sonra banka bilgilerini göreceksiniz; ödeme onaylanınca hazırlanır.</span></>}
                        {o.method === 'CashOnDelivery' && <><br /><span className="store-note">Tutarı kapıda ödersiniz.</span></>}
                        {o.method === 'Card' && <><br /><span className="store-note">Ödeme sayfasına yönlendirilirsiniz.</span></>}
                      </span>
                    </label></li>
                  ))}
                </ul>
              )}
            </section>

            <section className="ps-card store-panel account-card">
              <label className="field"><span className="field__label">Sipariş notu (isteğe bağlı)</span><textarea className="ps-input" rows={2} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} /></label>
            </section>
          </div>

          <aside className="ps-card store-panel cart-side">
            <h2>Sipariş özeti</h2>
            <ul className="cart-mini">{quote.lines.map((l) => <li key={l.productId}><span>{l.quantity} × {l.name}</span><span>{formatMoney(l.lineTotal)}</span></li>)}</ul>
            <Summary quote={quote} method={method} />
            {error && <p role="alert" className="store-error">{error}</p>}
            <button type="submit" className="ps-btn ps-btn--primary" disabled={busy || quote.hasIssues || !method || !addressId || quote.shippingOptions.length === 0}>{busy ? 'Sipariş veriliyor…' : 'Siparişi onayla'}</button>
            <p className="store-note">Siparişi onaylayarak mağazanın satış koşullarını kabul etmiş olursunuz.</p>
          </aside>
        </form>
      )}
    </div>
  )
}

export function OrderDonePage() {
  const { id } = useParams()
  const { api } = useCustomer()
  const [order, setOrder] = useState<OrderDetail>()
  const [error, setError] = useState<string>()
  usePageMeta({ title: 'Siparişiniz alındı', description: 'Sipariş onayı.', noindex: true })
  useEffect(() => {
    let current = true
    api<OrderDetail>(`/me/orders/${id}`).then((o) => { if (current) setOrder(o) }).catch((e: Error) => { if (current) setError(e.message) })
    return () => { current = false }
  }, [api, id])

  if (error) return <div className="store-empty"><Package size={40} aria-hidden /><h1>{error}</h1><Link to="/hesap/siparisler" className="ps-btn">Siparişlerime git</Link></div>
  if (!order) return <p className="store-note" aria-busy="true">Yükleniyor…</p>
  const method = order.paymentMethod ?? ''
  return (
    <div className="store-empty done">
      <CircleCheck size={44} aria-hidden />
      <h1>{order.status === 'Cancelled' ? 'Sipariş iptal edildi' : 'Siparişiniz alındı'}</h1>
      <p>Sipariş numaranız <strong>{order.orderNumber}</strong> · toplam <strong>{formatMoney(order.total, order.currency)}</strong></p>
      {order.status !== 'Cancelled' && method === 'BankTransfer' && order.paymentStatus === 'Pending' && (
        <div className="ps-card store-panel done__info">
          <h2>Havale / EFT bilgileri</h2>
          <p className="done__pre">{order.paymentInfo}</p>
          <p className="store-note">Açıklama kısmına sipariş numaranızı yazın. Ödemeniz onaylanınca siparişiniz hazırlanır.</p>
          {order.paymentExpiresAt && <p className="store-note"><strong>Son ödeme zamanı: {formatDateTime(order.paymentExpiresAt)}.</strong> Bu zamana kadar ödeme alınmazsa siparişiniz iptal edilir.</p>}
        </div>
      )}
      {method === 'CashOnDelivery' && <p className="store-note">Tutarı ({formatMoney(order.total, order.currency)}) teslimat sırasında ödeyeceksiniz.</p>}
      {method === 'Card' && order.paymentStatus === 'Paid' && <p className="store-note">Ödemeniz alındı.</p>}
      <div className="done__actions">
        <Link to={`/hesap/siparisler/${order.id}`} className="ps-btn ps-btn--primary">Siparişimi görüntüle</Link>
        <Link to="/" className="ps-btn">Alışverişe devam et</Link>
      </div>
    </div>
  )
}

/** Deneme ödeme sayfası: yalnızca geliştirme/deneme sağlayıcısında (Payments:Provider=fake) vardır; gerçek para geçmez. */
export function FakePaymentPage() {
  const { id } = useParams()
  const { api } = useCustomer()
  const navigate = useNavigate()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  usePageMeta({ title: 'Deneme ödeme sayfası', description: 'Deneme ödemesi.', noindex: true })

  async function finish(success: boolean) {
    setBusy(true); setError(undefined)
    try {
      await api(`/me/orders/${id}/fake-payment`, { method: 'POST', body: JSON.stringify({ success }) })
      navigate(`/siparis-tamam/${id}`, { replace: true })
    } catch (e) { setError((e as Error).message); setBusy(false) }
  }

  return (
    <div className="ps-card store-panel auth-card">
      <h1>Deneme ödeme sayfası</h1>
      <p className="store-note">Bu sayfa yalnızca deneme içindir; gerçek ödeme alınmaz. Gerçek bir ödeme sağlayıcısı bağlandığında kart bilgileri sağlayıcının sayfasında girilir.</p>
      {error && <p role="alert" className="store-error">{error}</p>}
      <div className="done__actions">
        <button type="button" className="ps-btn ps-btn--primary" disabled={busy} onClick={() => void finish(true)}>Ödemeyi tamamla</button>
        <button type="button" className="ps-btn" disabled={busy} onClick={() => void finish(false)}>Ödeme başarısız olsun</button>
      </div>
    </div>
  )
}

