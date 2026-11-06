import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link, NavLink, Outlet, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { ChevronRight, Home, MapPin, Package, Pencil, Plus, ShoppingBag, Star, Trash2, Truck, User } from 'lucide-react'
import {
  MAX_ADDRESSES, addressSummary, emptyAddress, formatDate, orderStatusLabel, orderStatusTone, orderSteps, pageCount, validateAddress, validateName, validatePassword, validatePhone,
  type Address, type AddressForm, type FormErrors, type OrderDetail, type OrderPage,
} from './account'
import { Field } from './authPages'
import { useCustomer } from './CustomerContext'
import { paymentMethodLabel, paymentStatusLabel } from './cart'
import { formatMoney } from './shop'
import { usePageMeta } from './seo'
import { useShop } from './ShopContext'

/** Hesap sayfalarının ortak iskeleti: solda gezinme (profil, adresler, siparişler), sağda içerik. */
export function AccountLayout() {
  const { profile, logout } = useCustomer()
  const { info } = useShop()
  const navigate = useNavigate()
  usePageMeta({ title: `Hesabım | ${info.name}`, description: `${info.name} hesabınız.`, noindex: true })
  const link = ({ isActive }: { isActive: boolean }) => `account-nav__link${isActive ? ' account-nav__link--active' : ''}`
  return (
    <div className="account">
      <aside className="account-nav" aria-label="Hesap menüsü">
        <div className="account-nav__who">
          {profile?.avatarUrl ? <img src={profile.avatarUrl} alt="" className="account-avatar" referrerPolicy="no-referrer" /> : <span className="account-avatar account-avatar--empty"><User size={22} aria-hidden /></span>}
          <div><strong>{profile?.fullName}</strong><span className="store-note">{profile?.email}</span></div>
        </div>
        <nav>
          <NavLink to="/hesap" end className={link}><User size={18} aria-hidden /> Profilim</NavLink>
          <NavLink to="/hesap/adresler" className={link}><MapPin size={18} aria-hidden /> Adreslerim</NavLink>
          <NavLink to="/hesap/siparisler" className={link}><Package size={18} aria-hidden /> Siparişlerim</NavLink>
        </nav>
        <button type="button" className="store-link-btn" onClick={() => { logout(); navigate('/') }}>Çıkış yap</button>
      </aside>
      <section className="account-content"><Outlet /></section>
    </div>
  )
}

// ---------- profil ----------

export function ProfilePage() {
  const { profile, setProfile, api } = useCustomer()
  const [name, setName] = useState(profile?.fullName ?? '')
  const [phone, setPhone] = useState(profile?.phone ?? '')
  const [marketing, setMarketing] = useState(profile?.marketingConsent ?? false)
  const [errors, setErrors] = useState<{ fullName?: string; phone?: string }>({})
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [busy, setBusy] = useState(false)

  useEffect(() => { if (profile) { setName(profile.fullName); setPhone(profile.phone ?? ''); setMarketing(profile.marketingConsent) } }, [profile])
  if (!profile) return null

  async function save(e: FormEvent) {
    e.preventDefault()
    const found = { fullName: validateName(name) ?? undefined, phone: validatePhone(phone, false) ?? undefined }
    setErrors(found); setMessage(undefined)
    if (found.fullName || found.phone) return
    setBusy(true)
    try {
      setProfile(await api('/me', { method: 'PUT', body: JSON.stringify({ fullName: name.trim(), phone: phone.trim() || null, marketingConsent: marketing }) }))
      setMessage({ ok: true, text: 'Profiliniz güncellendi.' })
    } catch (err) { setMessage({ ok: false, text: (err as Error).message }) }
    finally { setBusy(false) }
  }

  return (
    <div className="account-stack">
      <section className="ps-card store-panel account-card" aria-labelledby="profil-baslik">
        <h1 id="profil-baslik">Profilim</h1>
        <form className="store-form" onSubmit={save} noValidate>
          <Field label="Ad soyad" autoComplete="name" value={name} onChange={(e) => setName(e.target.value)} error={errors.fullName} />
          <label className="field">
            <span className="field__label">E-posta</span>
            <input className="ps-input" value={profile.email} readOnly aria-readonly />
            <span className="field__hint">{profile.emailVerified ? 'E-postanız doğrulandı.' : 'E-posta adresi değiştirilemez.'}</span>
          </label>
          <Field label="Telefon" type="tel" autoComplete="tel" value={phone} onChange={(e) => setPhone(e.target.value)} error={errors.phone} />
          <label className="store-check"><input type="checkbox" checked={marketing} onChange={(e) => setMarketing(e.target.checked)} /><span>Kampanya ve bilgilendirme iletileri almak istiyorum.</span></label>
          {message && <p role={message.ok ? 'status' : 'alert'} className={message.ok ? 'store-ok' : 'store-error'}>{message.text}</p>}
          <div className="store-actions"><button type="submit" className="ps-btn ps-btn--primary" disabled={busy}>{busy ? 'Kaydediliyor…' : 'Kaydet'}</button></div>
        </form>
      </section>
      <PasswordCard hasPassword={profile.hasPassword} googleLinked={profile.googleLinked} onChanged={() => setProfile({ ...profile, hasPassword: true })} />
    </div>
  )
}

function PasswordCard({ hasPassword, googleLinked, onChanged }: { hasPassword: boolean; googleLinked: boolean; onChanged: () => void }) {
  const { api } = useCustomer()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [error, setError] = useState<string>()
  const [ok, setOk] = useState(false)
  const [busy, setBusy] = useState(false)

  async function save(e: FormEvent) {
    e.preventDefault()
    setOk(false)
    const invalid = validatePassword(next) ?? (hasPassword && !current ? 'Mevcut parolayı girin.' : null)
    if (invalid) { setError(invalid); return }
    setBusy(true); setError(undefined)
    try {
      await api('/me/password', { method: 'PUT', body: JSON.stringify({ current: hasPassword ? current : null, new: next }) })
      setCurrent(''); setNext(''); setOk(true); onChanged()
    } catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }

  return (
    <section className="ps-card store-panel account-card" aria-labelledby="parola-baslik">
      <h2 id="parola-baslik">{hasPassword ? 'Parolayı değiştir' : 'Parola belirle'}</h2>
      {googleLinked && <p className="store-note">Google hesabınız bağlı: Google ile giriş yapabilirsiniz.{hasPassword ? '' : ' İsterseniz e-posta ve parolayla girmek için bir parola da belirleyebilirsiniz.'}</p>}
      <form className="store-form" onSubmit={save} noValidate>
        {hasPassword && <Field label="Mevcut parola" type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} />}
        <Field label="Yeni parola" type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} hint="En az 8 karakter" error={error} />
        {ok && <p role="status" className="store-ok">Parolanız kaydedildi.</p>}
        <div className="store-actions"><button type="submit" className="ps-btn" disabled={busy}>{busy ? 'Kaydediliyor…' : hasPassword ? 'Parolayı değiştir' : 'Parolayı belirle'}</button></div>
      </form>
    </section>
  )
}

// ---------- adresler ----------

function AddressEditor({ initial, onSave, onCancel }: { initial: AddressForm; onSave: (a: AddressForm) => Promise<void>; onCancel: () => void }) {
  const [form, setForm] = useState<AddressForm>(initial)
  const [errors, setErrors] = useState<FormErrors<AddressForm>>({})
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const set = (key: keyof AddressForm) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [key]: e.target.value }))

  async function submit(e: FormEvent) {
    e.preventDefault()
    const found = validateAddress(form)
    setErrors(found); setError(undefined)
    if (Object.keys(found).length > 0) return
    setBusy(true)
    try { await onSave({ ...form, postalCode: (form.postalCode ?? '').trim() || null }) }
    catch (err) { setError((err as Error).message); setBusy(false) }
  }

  return (
    <form className="ps-card store-panel store-form store-form--grid address-form" onSubmit={submit} noValidate aria-label="Adres formu">
      <Field label="Adres başlığı" placeholder="Ev, İş…" value={form.title} onChange={set('title')} error={errors.title} />
      <Field label="Teslim alacak kişi" autoComplete="name" value={form.fullName} onChange={set('fullName')} error={errors.fullName} />
      <Field label="Telefon" type="tel" autoComplete="tel" value={form.phone} onChange={set('phone')} error={errors.phone} />
      <Field label="İl" autoComplete="address-level1" value={form.city} onChange={set('city')} error={errors.city} />
      <Field label="İlçe" autoComplete="address-level2" value={form.district} onChange={set('district')} error={errors.district} />
      <Field label="Posta kodu (isteğe bağlı)" inputMode="numeric" maxLength={5} value={form.postalCode ?? ''} onChange={(e) => setForm((f) => ({ ...f, postalCode: e.target.value.replace(/\D/g, '').slice(0, 5) }))} error={errors.postalCode} />
      <div className="store-form__wide">
        <Field label="Açık adres" autoComplete="street-address" value={form.addressLine} onChange={set('addressLine')} error={errors.addressLine} />
        <label className="store-check"><input type="checkbox" checked={form.isDefault} onChange={(e) => setForm((f) => ({ ...f, isDefault: e.target.checked }))} /><span>Varsayılan teslimat adresi yap</span></label>
        {error && <p role="alert" className="store-error">{error}</p>}
        <div className="store-actions">
          <button type="submit" className="ps-btn ps-btn--primary" disabled={busy}>{busy ? 'Kaydediliyor…' : 'Adresi kaydet'}</button>
          <button type="button" className="ps-btn" onClick={onCancel}>Vazgeç</button>
        </div>
      </div>
    </form>
  )
}

export function AddressesPage() {
  const { api } = useCustomer()
  const [items, setItems] = useState<Address[]>()
  const [error, setError] = useState<string>()
  const [editing, setEditing] = useState<'new' | Address | null>(null)
  const [removing, setRemoving] = useState<string>()

  const load = useCallback(() => { api<Address[]>('/me/addresses').then(setItems).catch((e: Error) => setError(e.message)) }, [api])
  useEffect(load, [load])

  async function save(form: AddressForm) {
    if (editing === 'new') await api('/me/addresses', { method: 'POST', body: JSON.stringify(form) })
    else if (editing) await api(`/me/addresses/${editing.id}`, { method: 'PUT', body: JSON.stringify(form) })
    setEditing(null); load()
  }

  async function remove(id: string) {
    try { await api(`/me/addresses/${id}`, { method: 'DELETE' }); setRemoving(undefined); load() }
    catch (e) { setError((e as Error).message) }
  }

  if (editing) {
    return (
      <div className="account-stack">
        <h1 className="account-title">{editing === 'new' ? 'Yeni adres' : 'Adresi düzenle'}</h1>
        <AddressEditor initial={editing === 'new' ? { ...emptyAddress, isDefault: (items?.length ?? 0) === 0 } : { ...editing }} onSave={save} onCancel={() => setEditing(null)} />
      </div>
    )
  }

  return (
    <div className="account-stack">
      <div className="account-head">
        <h1 className="account-title">Adreslerim</h1>
        <button type="button" className="ps-btn ps-btn--primary" disabled={(items?.length ?? 0) >= MAX_ADDRESSES} onClick={() => setEditing('new')}><Plus size={16} aria-hidden /> Yeni adres</button>
      </div>
      {error && <p role="alert" className="store-error">{error}</p>}
      {!items ? <p className="store-note" aria-busy="true">Yükleniyor…</p> : items.length === 0 ? (
        <div className="store-empty"><MapPin size={40} aria-hidden /><h2>Kayıtlı adresiniz yok</h2><p className="store-note">Siparişlerinizde hızlıca seçebilmek için bir teslimat adresi ekleyin.</p></div>
      ) : (
        <ul className="address-list">
          {items.map((a) => (
            <li key={a.id} className={`ps-card address-card${a.isDefault ? ' address-card--default' : ''}`}>
              <div className="address-card__head">
                <strong><Home size={16} aria-hidden /> {a.title}</strong>
                {a.isDefault && <span className="store-chip store-chip--removable"><Star size={12} aria-hidden /> Varsayılan</span>}
              </div>
              <p>{a.fullName} · {a.phone}</p>
              <p className="store-note">{addressSummary(a)}</p>
              {removing === a.id ? (
                <div className="store-actions" role="alertdialog" aria-label="Adres silinsin mi">
                  <span>Bu adres silinsin mi?</span>
                  <button type="button" className="ps-btn" onClick={() => void remove(a.id)}>Sil</button>
                  <button type="button" className="ps-btn" onClick={() => setRemoving(undefined)}>Vazgeç</button>
                </div>
              ) : (
                <div className="store-actions">
                  <button type="button" className="store-link-btn" onClick={() => setEditing(a)}><Pencil size={14} aria-hidden /> Düzenle</button>
                  <button type="button" className="store-link-btn" onClick={() => setRemoving(a.id)}><Trash2 size={14} aria-hidden /> Sil</button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

// ---------- siparişler ----------

function StatusChip({ status }: { status: string }) {
  return <span className={`order-status order-status--${orderStatusTone(status)}`}>{orderStatusLabel[status] ?? status}</span>
}

export function OrdersPage() {
  const { api } = useCustomer()
  const [search, setSearch] = useSearchParams()
  const page = Math.max(1, Number(search.get('page')) || 1)
  const [data, setData] = useState<OrderPage>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    let current = true
    setError(undefined)
    api<OrderPage>(`/me/orders?page=${page}`).then((d) => { if (current) setData(d) }).catch((e: Error) => { if (current) setError(e.message) })
    return () => { current = false }
  }, [api, page])

  const pages = data ? pageCount(data.total, data.pageSize) : 1
  return (
    <div className="account-stack">
      <h1 className="account-title">Siparişlerim {data && <span className="store-note">{data.total} sipariş</span>}</h1>
      {error && <p role="alert" className="store-error">{error}</p>}
      {!data && !error ? <p className="store-note" aria-busy="true">Yükleniyor…</p> : data && data.items.length === 0 ? (
        <div className="store-empty"><ShoppingBag size={40} aria-hidden /><h2>Henüz siparişiniz yok</h2><p className="store-note">Verdiğiniz siparişler burada listelenir.</p><Link to="/" className="ps-btn">Alışverişe başla</Link></div>
      ) : data && (
        <>
          <ul className="order-list">
            {data.items.map((o) => (
              <li key={o.id}>
                <Link to={`/hesap/siparisler/${o.id}`} className="ps-card order-row">
                  {o.coverUrl ? <img className="order-row__img" src={o.coverUrl} alt="" loading="lazy" /> : <span className="order-row__img order-row__img--empty"><Package size={22} aria-hidden /></span>}
                  <div className="order-row__main">
                    <strong>{o.orderNumber}</strong>
                    <span className="store-note">{formatDate(o.orderDate)}</span>
                    <span>{o.firstItemName}{o.itemCount > 1 ? ` ve ${o.itemCount - 1} ürün daha` : ''}</span>
                  </div>
                  <div className="order-row__side"><StatusChip status={o.status} /><strong>{formatMoney(o.total, o.currency)}</strong></div>
                  <ChevronRight size={18} aria-hidden className="order-row__chev" />
                </Link>
              </li>
            ))}
          </ul>
          {pages > 1 && (
            <nav className="store-pager" aria-label="Sayfalar">
              <button type="button" className="ps-btn" disabled={page <= 1} onClick={() => setSearch({ page: String(page - 1) })}>Önceki</button>
              <span className="store-note">{page} / {pages}</span>
              <button type="button" className="ps-btn" disabled={page >= pages} onClick={() => setSearch({ page: String(page + 1) })}>Sonraki</button>
            </nav>
          )}
        </>
      )}
    </div>
  )
}

/** Müşteri iptali: tek tıkla değil, önce onay ister; iptalden sonra güncel sipariş yüklenir. */
function CancelOrder({ id, onDone }: { id: string; onDone: (o: OrderDetail) => void }) {
  const { api } = useCustomer()
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  async function cancel() {
    setBusy(true); setError(undefined)
    try {
      await api(`/me/orders/${id}/cancel`, { method: 'POST' })
      onDone(await api<OrderDetail>(`/me/orders/${id}`))
    } catch (e) { setError((e as Error).message); setBusy(false) }
  }

  return (
    <section className="ps-card store-panel account-card" aria-labelledby="iptal-baslik">
      <h2 id="iptal-baslik">Siparişi iptal et</h2>
      {!confirming ? (
        <>
          <p className="store-note">Sipariş hazırlanmaya başlamadan önce iptal edebilirsiniz.</p>
          <div><button type="button" className="ps-btn" onClick={() => setConfirming(true)}>Siparişi iptal et</button></div>
        </>
      ) : (
        <>
          <p>Bu siparişi iptal etmek istediğinize emin misiniz? Bu işlem geri alınamaz.</p>
          {error && <p role="alert" className="store-error">{error}</p>}
          <div className="done__actions">
            <button type="button" className="ps-btn" disabled={busy} onClick={() => void cancel()}>{busy ? 'İptal ediliyor…' : 'Evet, iptal et'}</button>
            <button type="button" className="ps-btn" disabled={busy} onClick={() => setConfirming(false)}>Vazgeç</button>
          </div>
        </>
      )}
    </section>
  )
}

export function OrderDetailPage() {
  const { id } = useParams()
  const { api } = useCustomer()
  const [order, setOrder] = useState<OrderDetail>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    let current = true
    setOrder(undefined); setError(undefined)
    api<OrderDetail>(`/me/orders/${id}`).then((d) => { if (current) setOrder(d) }).catch((e: Error) => { if (current) setError(e.message) })
    return () => { current = false }
  }, [api, id])

  if (error) return <div className="store-empty"><Package size={40} aria-hidden /><h1>{error}</h1><Link to="/hesap/siparisler" className="ps-btn">Siparişlerime dön</Link></div>
  if (!order) return <p className="store-note" aria-busy="true">Yükleniyor…</p>

  const steps = orderSteps(order.status)
  return (
    <div className="account-stack">
      <nav className="store-crumbs" aria-label="Yol"><Link to="/hesap/siparisler">Siparişlerim</Link><span><ChevronRight size={14} aria-hidden /> <strong>{order.orderNumber}</strong></span></nav>
      <div className="account-head"><h1 className="account-title">{order.orderNumber}</h1><StatusChip status={order.status} /></div>
      <p className="store-note">{formatDate(order.orderDate)} tarihinde verildi</p>
      {order.paymentMethod && (
        <p className="order-pay"><span>{paymentMethodLabel[order.paymentMethod] ?? order.paymentMethod}</span>{order.paymentStatus && !(order.status === 'Cancelled' && order.paymentStatus === 'Failed') && <span className={`order-status order-status--${order.paymentStatus === 'Paid' ? 'success' : order.paymentStatus === 'Failed' || order.paymentStatus === 'Expired' ? 'danger' : 'progress'}`}>{paymentStatusLabel[order.paymentStatus] ?? order.paymentStatus}</span>}</p>
      )}
      {order.paymentInfo && <section className="ps-card store-panel account-card"><h2>Havale / EFT bilgileri</h2><p className="done__pre">{order.paymentInfo}</p></section>}

      {steps.length > 0 && (
        <ol className="order-steps" aria-label="Sipariş durumu">
          {steps.map((s) => <li key={s.label} className={`order-steps__item order-steps__item--${s.state}`} aria-current={s.state === 'current' ? 'step' : undefined}>{s.label}</li>)}
        </ol>
      )}
      {order.statusNote && <p className="store-note">Not: {order.statusNote}</p>}
      {order.trackingNumber
        ? <p className="order-track"><Truck size={18} aria-hidden /> {order.shipmentCarrier} · takip no <strong>{order.trackingNumber}</strong>{order.trackingUrl && /^https:\/\//i.test(order.trackingUrl) && <> · <a href={order.trackingUrl} target="_blank" rel="noopener noreferrer">Kargom nerede?</a></>}</p>
        : order.shipmentCarrier && order.status !== 'Cancelled' && <p className="store-note"><Truck size={14} aria-hidden /> Kargo firması: {order.shipmentCarrier}</p>}

      <section className="ps-card store-panel account-card" aria-labelledby="urunler-baslik">
        <h2 id="urunler-baslik">Ürünler</h2>
        <ul className="order-lines">
          {order.lines.map((l, i) => (
            <li key={`${l.name}-${i}`} className="order-line">
              {l.coverUrl ? <img className="order-row__img" src={l.coverUrl} alt="" loading="lazy" /> : <span className="order-row__img order-row__img--empty"><Package size={20} aria-hidden /></span>}
              <div className="order-line__name">{l.productId ? <Link to={`/urun/${l.productId}`}>{l.name}</Link> : l.name}<span className="store-note">{l.quantity} adet × {formatMoney(l.unitPrice, order.currency)}</span></div>
              <strong>{formatMoney(l.unitPrice * l.quantity, order.currency)}</strong>
            </li>
          ))}
        </ul>
        {order.shippingFee !== null && <div className="order-fee"><span>Kargo</span><span>{order.shippingFee === 0 ? 'Ücretsiz' : formatMoney(order.shippingFee, order.currency)}</span></div>}
        {order.paymentFee ? <div className="order-fee"><span>Kapıda ödeme ücreti</span><span>{formatMoney(order.paymentFee, order.currency)}</span></div> : null}
        <div className="order-total"><span>Genel toplam (KDV dahil)</span><strong>{formatMoney(order.total, order.currency)}</strong></div>
      </section>

      {order.canCancel && <CancelOrder id={order.id} onDone={setOrder} />}

      {order.shippingAddress && <section className="ps-card store-panel account-card"><h2>Teslimat adresi</h2><p>{order.shippingAddress}</p></section>}
    </div>
  )
}
