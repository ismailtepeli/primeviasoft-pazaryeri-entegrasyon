import { useCallback, useEffect, useState } from 'react'
import { Trash2 } from 'lucide-react'
import { api } from '../api/client'
import type { DealerAccountView } from '../api/types'
import { Button, TextInput } from '../components'
import { balanceLabel, money, validatePaymentForm } from './b2bOrderView'
import './B2BOrderPanel.css'

/** Bayi cari hesabı: bakiye, vadesi geçen tutar, vade günü ve elle girilen ödemeler. Ödemeler en eski siparişten kapatılır. */
export default function DealerAccount({ dealerId }: { dealerId: string }) {
  const [account, setAccount] = useState<DealerAccountView>()
  const [amount, setAmount] = useState('')
  const [note, setNote] = useState('')
  const [term, setTerm] = useState('')
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()

  const load = useCallback(() => api<DealerAccountView>(`/b2b/dealers/${dealerId}/account`).then((a) => { setAccount(a); setTerm(String(a.paymentTermDays)) }).catch((e: Error) => setMessage({ ok: false, text: e.message })), [dealerId])
  useEffect(() => { void load() }, [load])

  async function call(path: string, init: RequestInit, ok: string) {
    try { const a = await api<DealerAccountView>(`/b2b/dealers/${dealerId}/${path}`, init); setAccount(a); setTerm(String(a.paymentTermDays)); setMessage({ ok: true, text: ok }); return true }
    catch (e) { setMessage({ ok: false, text: (e as Error).message }); return false }
  }

  async function addPayment() {
    const error = validatePaymentForm(amount)
    if (error) { setMessage({ ok: false, text: error }); return }
    if (await call('payments', { method: 'POST', body: JSON.stringify({ amount: Number(amount.replace(',', '.')), note: note.trim() || null }) }, 'Ödeme kaydedildi.')) { setAmount(''); setNote('') }
  }

  if (!account) return <section className="dealer-users"><h3>Cari hesap</h3>{message ? <p className="dealer-error" role="alert">{message.text}</p> : <p className="dealer-cell__meta">Yükleniyor…</p>}</section>
  const s = account.summary
  return (
    <section className="dealer-users" aria-labelledby="cari-title">
      <h3 id="cari-title">Cari hesap</h3>
      <dl className="b2b-order__meta">
        <div><dt>Toplam sipariş (iptaller hariç)</dt><dd>{money.format(s.totalOrders)}</dd></div>
        <div><dt>Toplam ödeme</dt><dd>{money.format(s.totalPayments)}</dd></div>
        <div><dt>Bakiye</dt><dd><strong>{money.format(Math.abs(s.balance))}</strong> <span className="b2b-order__muted">{balanceLabel(s.balance)}</span></dd></div>
        <div><dt>Vadesi geçen</dt><dd className={s.overdue > 0 ? 'b2b-order__error' : undefined}>{money.format(s.overdue)}{s.oldestUnpaidDate && <span className="b2b-order__muted"> · en eski ödenmemiş sipariş {new Date(s.oldestUnpaidDate).toLocaleDateString('tr-TR')}</span>}</dd></div>
      </dl>
      <div className="dealer-form">
        <TextInput label="Vade (gün)" inputMode="numeric" value={term} onChange={(e) => setTerm(e.target.value)} />
        <div style={{ alignSelf: 'end' }}><Button onClick={() => call('term', { method: 'PUT', body: JSON.stringify({ paymentTermDays: Number(term) }) }, 'Vade kaydedildi.')}>Vadeyi kaydet</Button></div>
      </div>
      <div className="dealer-form">
        <TextInput label="Ödeme tutarı (₺)" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
        <TextInput label="Not" value={note} onChange={(e) => setNote(e.target.value)} />
        <div style={{ alignSelf: 'end' }}><Button onClick={addPayment}>Ödeme ekle</Button></div>
      </div>
      {message && <p className={message.ok ? 'dealer-ok' : 'dealer-error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      {account.payments.length > 0 && (
        <ul className="dealer-users__list">
          {account.payments.map((p) => (
            <li key={p.id} className="dealer-users__item">
              <span className="dealer-cell"><strong>{money.format(p.amount)}</strong><span className="dealer-cell__meta">{new Date(p.paidAt).toLocaleDateString('tr-TR')}{p.note ? ` · ${p.note}` : ''}</span></span>
              <button type="button" className="icon-btn" aria-label={`${money.format(p.amount)} tutarındaki ödemeyi sil`} onClick={() => call(`payments/${p.id}`, { method: 'DELETE' }, 'Ödeme silindi.')}><Trash2 size={16} aria-hidden /></button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
