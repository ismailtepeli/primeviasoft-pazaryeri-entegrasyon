import { useState, type FormEvent, type InputHTMLAttributes } from 'react'
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { ShopApiError } from './api'
import { safeNext, validateEmail, validateRegister, type FormErrors, type RegisterForm } from './account'
import { useCustomer } from './CustomerContext'
import { GoogleButton } from './GoogleButton'
import { usePageMeta } from './seo'
import { useShop } from './ShopContext'

export function Field({ label, error, hint, ...rest }: { label: string; error?: string; hint?: string } & InputHTMLAttributes<HTMLInputElement>) {
  return (
    <label className="field">
      <span className="field__label">{label}</span>
      <input className="ps-input" aria-invalid={!!error} {...rest} />
      {error ? <span className="field__error">{error}</span> : hint ? <span className="field__hint">{hint}</span> : null}
    </label>
  )
}

/** Yalnızca oturum açmış müşteriye açık sayfalar; açmamışsa girişe yollar ve girişten sonra geri getirir. */
export function RequireLogin({ children }: { children: React.ReactNode }) {
  const { token, ready } = useCustomer()
  const location = useLocation()
  if (!ready) return <p className="store-note" aria-busy="true">Yükleniyor…</p>
  if (!token) return <Navigate to={`/giris?next=${encodeURIComponent(location.pathname + location.search)}`} replace />
  return <>{children}</>
}

/** Google ile devam: ilk kez kayıt olanlardan üyelik koşulu kabulü istenir (sunucu `terms_required` döner). */
function GoogleSection({ onDone }: { onDone: () => void }) {
  const { googleClientId, googleLogin } = useCustomer()
  const [pending, setPending] = useState<string | null>(null)
  const [terms, setTerms] = useState(false)
  const [marketing, setMarketing] = useState(false)
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  if (!googleClientId) return null

  async function submit(idToken: string, acceptTerms: boolean) {
    setBusy(true); setError(undefined)
    try { await googleLogin(idToken, acceptTerms, marketing, true); onDone() }
    catch (e) {
      if (e instanceof ShopApiError && e.code === 'terms_required') { setPending(idToken); setError(undefined) }
      else setError((e as Error).message)
    } finally { setBusy(false) }
  }

  return (
    <div className="auth-google">
      <div className="auth-divider"><span>veya</span></div>
      {pending ? (
        <div className="store-form">
          <p className="store-note">Google hesabınız ilk kez bu mağazada kullanılıyor. Hesabınızı oluşturmak için onayınız gerekir.</p>
          <label className="store-check"><input type="checkbox" checked={terms} onChange={(e) => setTerms(e.target.checked)} /><span>Üyelik koşullarını ve aydınlatma metnini okudum, kabul ediyorum.</span></label>
          <label className="store-check"><input type="checkbox" checked={marketing} onChange={(e) => setMarketing(e.target.checked)} /><span>Kampanya ve bilgilendirme iletileri almak istiyorum (isteğe bağlı).</span></label>
          <button type="button" className="ps-btn ps-btn--primary" disabled={!terms || busy} onClick={() => void submit(pending, true)}>{busy ? 'Devam ediliyor…' : 'Hesabımı oluştur'}</button>
        </div>
      ) : <GoogleButton clientId={googleClientId} onCredential={(t) => void submit(t, false)} />}
      {error && <p role="alert" className="store-error">{error}</p>}
    </div>
  )
}

export function LoginPage() {
  const { token, login } = useCustomer()
  const { info } = useShop()
  const navigate = useNavigate()
  const [search] = useSearchParams()
  const next = safeNext(search.get('next'))
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [remember, setRemember] = useState(true)
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  usePageMeta({ title: `Giriş yap | ${info.name}`, description: `${info.name} hesabınıza giriş yapın.`, noindex: true })

  if (token) return <Navigate to={next} replace />

  async function submit(e: FormEvent) {
    e.preventDefault()
    const invalid = validateEmail(email) ?? (password ? null : 'Parola zorunlu.')
    if (invalid) { setError(invalid); return }
    setBusy(true); setError(undefined)
    try { await login(email.trim(), password, remember); navigate(next, { replace: true }) }
    catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }

  return (
    <div className="ps-card store-panel auth-card">
      <h1>Giriş yap</h1>
      <form className="store-form" onSubmit={submit} noValidate>
        <Field label="E-posta" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} />
        <Field label="Parola" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} error={error} />
        <label className="store-remember"><input type="checkbox" checked={remember} onChange={(e) => setRemember(e.target.checked)} /><span>Beni hatırla</span></label>
        <button type="submit" className="ps-btn ps-btn--primary" disabled={busy}>{busy ? 'Giriş yapılıyor…' : 'Giriş yap'}</button>
      </form>
      <GoogleSection onDone={() => navigate(next, { replace: true })} />
      <p className="store-note">Hesabınız yok mu? <Link to={`/kayit?next=${encodeURIComponent(next)}`}>Kayıt olun</Link></p>
    </div>
  )
}

export function RegisterPage() {
  const { token, register } = useCustomer()
  const { info } = useShop()
  const navigate = useNavigate()
  const [search] = useSearchParams()
  const next = safeNext(search.get('next'))
  const [form, setForm] = useState<RegisterForm>({ fullName: '', email: '', password: '', phone: '', acceptTerms: false })
  const [marketing, setMarketing] = useState(false)
  const [errors, setErrors] = useState<FormErrors<RegisterForm>>({})
  const [serverError, setServerError] = useState<string>()
  const [busy, setBusy] = useState(false)
  usePageMeta({ title: `Kayıt ol | ${info.name}`, description: `${info.name} mağazasında hesap oluşturun; siparişlerinizi ve adreslerinizi tek yerden yönetin.`, noindex: true })

  if (token) return <Navigate to={next} replace />
  const set = (key: keyof RegisterForm) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [key]: e.target.value }))

  async function submit(e: FormEvent) {
    e.preventDefault()
    const found = validateRegister(form)
    setErrors(found); setServerError(undefined)
    if (Object.keys(found).length > 0) return
    setBusy(true)
    try { await register({ ...form, email: form.email.trim(), marketingConsent: marketing }, true); navigate(next, { replace: true }) }
    catch (err) { setServerError((err as Error).message) }
    finally { setBusy(false) }
  }

  return (
    <div className="ps-card store-panel auth-card">
      <h1>Hesap oluştur</h1>
      <form className="store-form" onSubmit={submit} noValidate>
        <Field label="Ad soyad" autoComplete="name" value={form.fullName} onChange={set('fullName')} error={errors.fullName} />
        <Field label="E-posta" type="email" autoComplete="email" value={form.email} onChange={set('email')} error={errors.email} />
        <Field label="Parola" type="password" autoComplete="new-password" value={form.password} onChange={set('password')} error={errors.password} hint="En az 8 karakter" />
        <Field label="Telefon (isteğe bağlı)" type="tel" autoComplete="tel" value={form.phone} onChange={set('phone')} error={errors.phone} />
        <label className="store-check"><input type="checkbox" checked={form.acceptTerms} onChange={(e) => setForm((f) => ({ ...f, acceptTerms: e.target.checked }))} aria-invalid={!!errors.acceptTerms} /><span>Üyelik koşullarını ve aydınlatma metnini okudum, kabul ediyorum.</span></label>
        {errors.acceptTerms && <span className="field__error">{errors.acceptTerms}</span>}
        <label className="store-check"><input type="checkbox" checked={marketing} onChange={(e) => setMarketing(e.target.checked)} /><span>Kampanya ve bilgilendirme iletileri almak istiyorum (isteğe bağlı).</span></label>
        {serverError && <p role="alert" className="store-error">{serverError}</p>}
        <button type="submit" className="ps-btn ps-btn--primary" disabled={busy}>{busy ? 'Hesap oluşturuluyor…' : 'Kayıt ol'}</button>
      </form>
      <GoogleSection onDone={() => navigate(next, { replace: true })} />
      <p className="store-note">Zaten hesabınız var mı? <Link to={`/giris?next=${encodeURIComponent(next)}`}>Giriş yapın</Link></p>
    </div>
  )
}
