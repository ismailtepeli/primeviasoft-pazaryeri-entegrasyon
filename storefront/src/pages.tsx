import { useState, type ChangeEvent, type FormEvent, type InputHTMLAttributes, type ReactNode } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { StoreApiError, storeApi } from './api'
import { useStore } from './StoreContext'
import { emptyApplyForm, validateApply, validateLogin, type ApplyForm } from './validation'

function Field({ label, error, hint, ...rest }: { label: string; error?: string; hint?: string } & InputHTMLAttributes<HTMLInputElement>) {
  return (
    <label className="field">
      <span className="field__label">{label}</span>
      <input className="ps-input" aria-invalid={!!error} {...rest} />
      {error ? <span className="field__error">{error}</span> : hint ? <span className="field__hint">{hint}</span> : null}
    </label>
  )
}

function Panel({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="ps-card store-panel" aria-labelledby="panel-title">
      <h1 id="panel-title">{title}</h1>
      {children}
    </section>
  )
}

/** Giriş yapmamış bayiyi giriş sayfasına yollar. */
export function RequireLogin({ children }: { children: ReactNode }) {
  const { token } = useStore()
  const location = useLocation()
  if (!token) return <Navigate to="/giris" replace state={{ from: location.pathname }} />
  return <>{children}</>
}

export function LoginPage() {
  const { login, token, info } = useStore()
  const navigate = useNavigate()
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/'
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [message, setMessage] = useState<string>()
  const [busy, setBusy] = useState(false)

  if (token) return <Navigate to="/" replace />

  async function submit(e: FormEvent) {
    e.preventDefault()
    const found = validateLogin(email, password)
    setErrors(found)
    if (Object.keys(found).length > 0) return
    setBusy(true)
    setMessage(undefined)
    try {
      await login(email.trim(), password)
      navigate(from, { replace: true })
    } catch (err) {
      setMessage(err instanceof StoreApiError ? err.message : 'Giriş yapılamadı.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Panel title={`${info.name} bayi girişi`}>
      <form className="store-form" onSubmit={submit} noValidate>
        <Field label="E-posta" type="email" autoComplete="username" value={email} onChange={(e: ChangeEvent<HTMLInputElement>) => setEmail(e.target.value)} error={errors.email} />
        <Field label="Parola" type="password" autoComplete="current-password" value={password} onChange={(e: ChangeEvent<HTMLInputElement>) => setPassword(e.target.value)} error={errors.password} />
        {message && <p role="alert" className="store-error">{message}</p>}
        <button type="submit" className="ps-btn ps-btn--primary" disabled={busy}>{busy ? 'Giriş yapılıyor…' : 'Giriş yap'}</button>
      </form>
      <p className="store-note">Henüz bayimiz değil misiniz? <Link to="/basvuru">Bayi olmak için başvurun</Link>.</p>
    </Panel>
  )
}

export function ApplyPage() {
  const { tenant, info } = useStore()
  const [form, setForm] = useState<ApplyForm>(emptyApplyForm)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [busy, setBusy] = useState(false)
  const set = (key: keyof ApplyForm) => (e: ChangeEvent<HTMLInputElement>) => setForm((f) => ({ ...f, [key]: e.target.value }))

  async function submit(e: FormEvent) {
    e.preventDefault()
    const found = validateApply(form)
    setErrors(found)
    if (Object.keys(found).length > 0) return
    setBusy(true)
    setMessage(undefined)
    try {
      const trimmed = Object.fromEntries(Object.entries(form).map(([k, v]) => [k, k === 'password' ? v : v.trim()]))
      const r = await storeApi<{ message: string }>('/auth/apply', { method: 'POST', body: JSON.stringify({ tenantSlug: tenant.slug, ...trimmed }) })
      setMessage({ ok: true, text: r.message })
      setForm(emptyApplyForm)
    } catch (err) {
      setMessage({ ok: false, text: err instanceof StoreApiError ? err.message : 'Başvuru gönderilemedi.' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <Panel title={`${info.name} bayi başvurusu`}>
      <form className="store-form store-form--grid" onSubmit={submit} noValidate>
        <Field label="Firma adı" value={form.companyName} onChange={set('companyName')} error={errors.companyName} />
        <Field label="Vergi numarası" hint="VKN 10, TCKN 11 hane" inputMode="numeric" value={form.taxNumber} onChange={set('taxNumber')} error={errors.taxNumber} />
        <Field label="Vergi dairesi" value={form.taxOffice} onChange={set('taxOffice')} />
        <Field label="Yetkili adı soyadı" value={form.contactName} onChange={set('contactName')} />
        <Field label="E-posta" type="email" autoComplete="username" value={form.email} onChange={set('email')} error={errors.email} />
        <Field label="Telefon" type="tel" value={form.phone} onChange={set('phone')} />
        <Field label="Şehir" value={form.city} onChange={set('city')} />
        <Field label="Adres" value={form.address} onChange={set('address')} />
        <Field label="Parola" type="password" autoComplete="new-password" hint="En az 8 karakter" value={form.password} onChange={set('password')} error={errors.password} />
        <div className="store-form__wide">
          {message && <p role={message.ok ? 'status' : 'alert'} className={message.ok ? 'store-ok' : 'store-error'}>{message.text}</p>}
          <button type="submit" className="ps-btn ps-btn--primary" disabled={busy}>{busy ? 'Gönderiliyor…' : 'Başvuruyu gönder'}</button>
        </div>
      </form>
      <p className="store-note">Başvurunuz onaylandığında e-posta ve parolanızla giriş yapabilirsiniz. <Link to="/giris">Giriş yap</Link></p>
    </Panel>
  )
}

export function HomePage() {
  const { user, info } = useStore()
  return (
    <Panel title={`Hoş geldiniz${user?.fullName ? `, ${user.fullName}` : ''}`}>
      <p className="store-note">{info.name} bayi sitesine giriş yaptınız. Ürün listesi, sepet ve sipariş geçmişi yakında burada olacak.</p>
    </Panel>
  )
}

export function SoonPage({ title }: { title: string }) {
  return (
    <Panel title={title}>
      <p className="store-note">Bu bölüm henüz hazır değil.</p>
    </Panel>
  )
}

export function StatusPage({ title, text, action }: { title: string; text: string; action?: { label: string; onClick: () => void } }) {
  return (
    <main className="store-status">
      <section className="ps-card store-panel" role="status">
        <h1>{title}</h1>
        <p className="store-note">{text}</p>
        {action && <button type="button" className="ps-btn" onClick={action.onClick}>{action.label}</button>}
      </section>
    </main>
  )
}
