import { useId, useState, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes } from 'react'

interface FieldProps {
  label: string
  hint?: string
  error?: string
  children: (id: string, describedBy: string | undefined) => ReactNode
}

function Field({ label, hint, error, children }: FieldProps) {
  const id = useId()
  const msgId = error || hint ? `${id}-msg` : undefined
  return (
    <div className="field">
      <label className="field__label" htmlFor={id}>{label}</label>
      {children(id, msgId)}
      {error ? <span id={msgId} className="field__error">{error}</span>
        : hint ? <span id={msgId} className="field__hint">{hint}</span> : null}
    </div>
  )
}

type Meta = { label: string; hint?: string; error?: string }

export function TextInput({ label, hint, error, ...rest }: Meta & InputHTMLAttributes<HTMLInputElement>) {
  return (
    <Field label={label} hint={hint} error={error}>
      {(id, d) => <input id={id} className="ps-input" aria-describedby={d} aria-invalid={!!error} {...rest} />}
    </Field>
  )
}

/** Gizli bilgi (API anahtarı vb.) için maskeli giriş; "Göster" ile açılır. */
export function SecretInput({ label, hint, error, ...rest }: Meta & Omit<InputHTMLAttributes<HTMLInputElement>, 'type'>) {
  const [shown, setShown] = useState(false)
  return (
    <Field label={label} hint={hint} error={error}>
      {(id, d) => (
        <div className="secret">
          <input id={id} className="ps-input" type={shown ? 'text' : 'password'} autoComplete="off"
            aria-describedby={d} aria-invalid={!!error} {...rest} />
          <button type="button" className="secret__toggle" onClick={() => setShown(!shown)}>
            {shown ? 'Gizle' : 'Göster'}
          </button>
        </div>
      )}
    </Field>
  )
}

export function Select({ label, hint, error, children, ...rest }: Meta & SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <Field label={label} hint={hint} error={error}>
      {(id, d) => <select id={id} className="ps-input" aria-describedby={d} aria-invalid={!!error} {...rest}>{children}</select>}
    </Field>
  )
}
