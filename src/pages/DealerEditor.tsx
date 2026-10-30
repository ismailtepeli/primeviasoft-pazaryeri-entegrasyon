import { useEffect, useState, type ChangeEvent } from 'react'
import { api } from '../api/client'
import type { DealerDetail, DealerGroup, DealerUser } from '../api/types'
import { Button, Select, StatusBadge, TextInput } from '../components'
import { useModules } from '../modules/ModulesContext'
import { isModuleEnabled } from '../modules/moduleState'
import DealerAccount from './DealerAccount'
import { dealerStatusBadge, emptyDealerForm, formFromDealer, toDealerInput, validateDealerForm, validateUserForm, type DealerForm } from './dealerView'
import './Dealers.css'

interface Props {
  /** Düzenlenen bayi; yoksa yeni bayi eklenir. */
  dealerId?: string
  groups: DealerGroup[]
  onClose: () => void
  /** Kaydedilince ya da kullanıcı değişince liste yenilensin. */
  onChanged: () => void
}

/** Bayi ekleme/düzenleme paneli; kayıtlı bayide kullanıcılar da yönetilir. */
export default function DealerEditor({ dealerId, groups, onClose, onChanged }: Props) {
  const [dealer, setDealer] = useState<DealerDetail>()
  const [form, setForm] = useState<DealerForm>(emptyDealerForm)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [saving, setSaving] = useState(false)
  const [user, setUser] = useState({ email: '', fullName: '', password: '' })
  const [userErrors, setUserErrors] = useState<Record<string, string>>({})

  useEffect(() => {
    if (!dealerId) { setDealer(undefined); setForm(emptyDealerForm); return }
    let current = true
    api<DealerDetail>(`/dealers/${dealerId}`)
      .then((d) => { if (current) { setDealer(d); setForm(formFromDealer(d)) } })
      .catch((e: Error) => { if (current) setMessage({ ok: false, text: e.message }) })
    return () => { current = false }
  }, [dealerId])

  const set = (key: keyof DealerForm) => (e: ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setForm((f) => ({ ...f, [key]: e.target.value }))

  async function save() {
    const found = validateDealerForm(form)
    setErrors(found)
    if (Object.keys(found).length > 0) return
    setSaving(true); setMessage(undefined)
    try {
      const body = JSON.stringify(toDealerInput(form))
      const saved = dealerId
        ? await api<DealerDetail>(`/dealers/${dealerId}`, { method: 'PUT', body })
        : await api<DealerDetail>('/dealers', { method: 'POST', body })
      setDealer(saved)
      setMessage({ ok: true, text: dealerId ? 'Bayi kaydedildi.' : 'Bayi eklendi; şimdi kullanıcı ekleyebilirsiniz.' })
      onChanged()
      if (!dealerId) { /* yeni bayi kaydedilince kullanıcı bölümü açılsın */ }
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
    finally { setSaving(false) }
  }

  async function addUser() {
    const found = validateUserForm(user)
    setUserErrors(found)
    if (Object.keys(found).length > 0 || !dealer) return
    try {
      await api(`/dealers/${dealer.id}/users`, { method: 'POST', body: JSON.stringify({ email: user.email.trim(), fullName: user.fullName.trim(), password: user.password, role: 'Buyer' }) })
      setUser({ email: '', fullName: '', password: '' })
      setDealer(await api<DealerDetail>(`/dealers/${dealer.id}`))
      setMessage({ ok: true, text: 'Kullanıcı eklendi.' })
      onChanged()
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
  }

  async function toggleUser(u: DealerUser) {
    if (!dealer) return
    try {
      await api(`/dealers/${dealer.id}/users/${u.id}`, { method: 'PUT', body: JSON.stringify({ isActive: !u.isActive }) })
      setDealer(await api<DealerDetail>(`/dealers/${dealer.id}`))
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
  }

  const badge = dealer ? dealerStatusBadge[dealer.status] : undefined
  const { enabled } = useModules()
  const accountOn = isModuleEnabled('b2b-store', enabled)
  return (
    <section className="ps-card dealer-editor" aria-label={dealerId ? 'Bayiyi düzenle' : 'Yeni bayi'}>
      <header className="dealer-editor__head">
        <h2>{dealerId ? dealer?.name ?? 'Bayi' : 'Yeni bayi'}</h2>
        {badge && <StatusBadge variant={badge.variant}>{badge.text}</StatusBadge>}
        <Button onClick={onClose}>Kapat</Button>
      </header>
      <div className="dealer-form">
        <TextInput label="Firma adı" value={form.name} onChange={set('name')} error={errors.name} />
        <TextInput label="Vergi numarası" hint="VKN 10, TCKN 11 hane" inputMode="numeric" value={form.taxNumber} onChange={set('taxNumber')} error={errors.taxNumber} />
        <TextInput label="Vergi dairesi" value={form.taxOffice} onChange={set('taxOffice')} />
        <TextInput label="Yetkili" value={form.contactName} onChange={set('contactName')} />
        <TextInput label="E-posta" type="email" value={form.email} onChange={set('email')} error={errors.email} />
        <TextInput label="Telefon" value={form.phone} onChange={set('phone')} />
        <TextInput label="Şehir" value={form.city} onChange={set('city')} />
        <Select label="Bayi grubu" value={form.groupId} onChange={set('groupId')}>
          <option value="">Grup yok</option>
          {groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
        </Select>
        <div className="dealer-form__wide"><TextInput label="Adres" value={form.address} onChange={set('address')} /></div>
        <div className="dealer-form__wide"><TextInput label="Not (yalnızca yönetici görür)" value={form.notes} onChange={set('notes')} /></div>
      </div>
      {message && <p className={message.ok ? 'dealer-ok' : 'dealer-error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      <div className="dealer-editor__actions">
        <Button onClick={save} disabled={saving}>{saving ? 'Kaydediliyor…' : 'Kaydet'}</Button>
      </div>

      {dealer && (
        <div className="dealer-users">
          <h3>Kullanıcılar</h3>
          {dealer.users.length === 0 && <p className="dealer-cell__meta">Henüz kullanıcı yok.</p>}
          <ul className="dealer-users__list">
            {dealer.users.map((u) => (
              <li key={u.id} className="dealer-users__item">
                <span className="dealer-cell"><strong>{u.fullName}</strong><span className="dealer-cell__meta">{u.email} · {u.role === 'Owner' ? 'Yetkili' : 'Alıcı'}</span></span>
                <StatusBadge variant={u.isActive ? 'success' : 'inactive'}>{u.isActive ? 'Açık' : 'Kapalı'}</StatusBadge>
                <Button onClick={() => toggleUser(u)}>{u.isActive ? 'Erişimi kapat' : 'Erişimi aç'}</Button>
              </li>
            ))}
          </ul>
          <div className="dealer-form">
            <TextInput label="Ad soyad" value={user.fullName} onChange={(e) => setUser({ ...user, fullName: e.target.value })} error={userErrors.fullName} />
            <TextInput label="E-posta" type="email" value={user.email} onChange={(e) => setUser({ ...user, email: e.target.value })} error={userErrors.email} />
            <TextInput label="Parola" type="password" autoComplete="new-password" hint="En az 8 karakter" value={user.password} onChange={(e) => setUser({ ...user, password: e.target.value })} error={userErrors.password} />
          </div>
          <div className="dealer-editor__actions"><Button onClick={addUser}>Kullanıcı ekle</Button></div>
        </div>
      )}
      {dealer && accountOn && <DealerAccount dealerId={dealer.id} />}
    </section>
  )
}
