import { useState, type FormEvent } from 'react'
import { Button, SecretInput, Select, TextInput } from '../components'
import { formFor, userAgentPreview, validate, type FieldDef } from '../channels/channelForms'
import './ChannelForm.css'

function Input({ field, value, error, onChange }: { field: FieldDef; value: string; error?: string; onChange: (v: string) => void }) {
  const common = { label: field.label, hint: field.hint, error, value }
  if (field.kind === 'secret') return <SecretInput {...common} onChange={(e) => onChange(e.target.value)} />
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
  onSubmit: (values: Record<string, string>) => void | Promise<void>
  onCancel: () => void
}

/** Kanala göre alanları değişen bağlantı formu. */
export default function ChannelForm({ channel, onSubmit, onCancel }: ChannelFormProps) {
  const def = formFor(channel)
  const [values, setValues] = useState<Record<string, string>>({ environment: 'live' })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)

  if (!def) {
    return (
      <section className="ps-card channel-form">
        <h2>{channel} bağlantısı</h2>
        <p className="channel-form__note">Bu kanal API anahtarıyla değil, kanal hesabınızla yetkilendirilerek bağlanır.</p>
        <div className="channel-form__actions"><Button onClick={onCancel}>Kapat</Button></div>
      </section>
    )
  }

  const preview = userAgentPreview(def, values)

  async function submit(e: FormEvent) {
    e.preventDefault()
    const found = validate(def!, values)
    setErrors(found)
    if (Object.keys(found).length > 0) return
    setBusy(true)
    try { await onSubmit(values) } finally { setBusy(false) }
  }

  return (
    <form className="ps-card channel-form" onSubmit={submit} noValidate>
      <h2>{channel} bağlantısı</h2>
      {def.fields.map((f) => (
        <Input key={f.name} field={f} value={values[f.name] ?? ''} error={errors[f.name]}
          onChange={(v) => setValues({ ...values, [f.name]: v })} />
      ))}
      {preview && (
        <p className="channel-form__ua">
          <span>User-Agent önizlemesi</span>
          <code>{preview}</code>
        </p>
      )}
      <div className="channel-form__actions">
        <Button onClick={onCancel}>Vazgeç</Button>
        <Button variant="primary" type="submit" disabled={busy}>Kaydet</Button>
      </div>
    </form>
  )
}
