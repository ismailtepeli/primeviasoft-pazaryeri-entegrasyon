import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router-dom'
import { Button, SecretInput, TextInput } from '../components'
import { useAuth } from '../auth/AuthContext'
import './Login.css'

export default function Login() {
  const { authenticated, login } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [remember, setRemember] = useState(false)
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  if (authenticated) return <Navigate to="/" replace />

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      await login(email, password, remember)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Giriş yapılamadı.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login">
      <form className="ps-card login__card" onSubmit={submit}>
        <h1>Giriş yap</h1>
        <TextInput label="E-posta" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} required />
        <SecretInput label="Parola" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required error={error} />
        <label className="login__remember">
          <input type="checkbox" checked={remember} onChange={(e) => setRemember(e.target.checked)} />
          <span>Beni hatırla</span>
        </label>
        <Button variant="primary" type="submit" disabled={busy}>{busy ? 'Giriş yapılıyor…' : 'Giriş yap'}</Button>
      </form>
    </div>
  )
}
