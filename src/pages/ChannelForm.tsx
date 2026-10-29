import { useEffect, useState, type FormEvent } from 'react'
import { api, ApiError } from '../api/client'
import type { CredentialView } from '../api/types'
import { Button, SecretInput, Select, TextInput } from '../components'
import { formFor, userAgentPreview, validate, type FieldDef } from '../channels/channelForms'
import './ChannelForm.css'

interface InputProps {
  field: FieldDef
  value: string
  error?: string
  /** Kayıtlı gizli değerin maskesi ("••••1234"); alanda placeholder olarak gösterilir. */
  masked?: string
  onChange: (v: string) => void
}

function Input({ field, value, error, masked, onChange }: InputProps) {
  const common = { label: field.label, hint: masked ? 'Kayıtlı. Değiştirmek için yeni değer girin.' : field.hint, error, value }
  if (field.kind === 'secret') return <SecretInput {...common} placeholder={masked} onChange={(e) => onChange(e.target.value)} />
  if (field.kind === 'environment') {
    return (
      <Select {...common} onChange={(e) => onChange(e.target.value)}>
        <option value="live">Canlı</option>
        <option value="staging">Test</option>
      </Select>
    )
  }
  return <TextInput {...common} onChange={(e) => onChange(e.target.value)} autoComplete="off" />
}

export interface ChannelFormProps {
  channel: string
  /** Başarısız olursa hata fırlatmalı; mesaj formda gösterilir. */
  onSubmit: (values: Record<string, string>) => void | Promise<void>
  onCancel: () => void
}

/** Kanala göre alanları değişen bağlantı formu. Seçili ortamın kayıtlı bilgileri maskeli gelir. */
export default function ChannelForm({ channel, onSubmit, onCancel }: ChannelFormProps) {
  const def = formFor(channel)
  const [values, setValues] = useState<Record<string, string>>({ environment: 'live' })
  const [stored, setStored] = useState<CredentialView>()
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [submitError, setSubmitError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const environment = values.environment

  // Ortam değişince (canlı/test) o ortamın kayıtlı bilgilerini getir; ikisi ayrı tutulur.
  useEffect(() => {
    if (!def) return
    let cancelled = false
    api<CredentialView>(`/channels/${encodeURIComponent(channel)}/credentials?environment=${environment}`)
      .then((view) => {
        if (cancelled) return
        setStored(view)
        // Açık alanları (satıcı kimliği gibi) doldur; gizli alanlar boş kalır, maske placeholder'da görünür.
        setValues((v) => ({ ...v, ...Object.fromEntries(view.fields.filter((f) => !f.secret && f.value).map((f) => [f.name, f.value!])) }))
      })
      .catch((e) => {
        if (cancelled) return
        setStored(undefined)
        if (!(e instanceof ApiError && e.status === 404)) setSubmitError(e.message)
      })
    return () => { cancelled = true }
  }, [channel, environment, def])

  if (!def) {
    return (
      <section className="ps-card channel-form">
        <h2>{channel} bağlantısı</h2>
        <p className="channel-form__note">Bu kanal API anahtarıyla değil, kanal hesabınızla yetkilendirilerek bağlanır.</p>
        <div className="channel-form__actions"><Button onClick={onCancel}>Kapat</Button></div>
      </section>
    )
  }

  const maskOf = (name: string) => stored?.fields.find((f) => f.name === name && f.secret)?.masked ?? undefined
  const storedSecrets = new Set(stored?.fields.filter((f) => f.secret).map((f) => f.name))
  const preview = userAgentPreview(def, values)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setSubmitError(undefined)
    const found = validate(def!, values, storedSecrets)
    setErrors(found)
    if (Object.keys(found).length > 0) return
    setBusy(true)
    try { await onSubmit(values) } catch (err) { setSubmitError(err instanceof Error ? err.message : 'Kaydedilemedi.') } finally { setBusy(false) }
  }

  return (
    <form className="ps-card channel-form" onSubmit={submit} noValidate>
      <h2>{channel} bağlantısı</h2>
      {def.fields.map((f) => (
        <Input key={f.name} field={f} value={values[f.name] ?? ''} error={errors[f.name]} masked={maskOf(f.name)}
          onChange={(v) => setValues({ ...values, [f.name]: v })} />
      ))}
      {preview && (
        <p className="channel-form__ua">
          <span>User-Agent önizlemesi</span>
          <code>{preview}</code>
        </p>
      )}
      {submitError && <p className="channel-form__error" role="alert">{submitError}</p>}
      <div className="channel-form__actions">
        <Button onClick={onCancel}>Vazgeç</Button>
        <Button variant="primary" type="submit" disabled={busy}>{busy ? 'Kaydediliyor…' : 'Kaydet'}</Button>
      </div>
    </form>
  )
}
