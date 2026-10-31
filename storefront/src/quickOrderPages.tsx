import { useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { ClipboardPaste, FileSpreadsheet, ShoppingCart } from 'lucide-react'
import { Link } from 'react-router-dom'
import { StoreApiError, storeApi } from './api'
import { useCart } from './CartContext'
import { countLines, errorsAsText, summarize, validateUpload, type QuickOrderResult } from './quickOrder'
import { useStore } from './StoreContext'

const example = 'KALEM-01;20\nDEFTER-05;10\n8690000000012 x 5'

/** Hızlı sipariş: kod/adet yapıştırma ya da Excel/CSV yükleme; geçerli satırlar sepete eklenir, sorunlular satır numarasıyla listelenir. */
export default function QuickOrderPage() {
  const { token } = useStore()
  const { replace } = useCart()
  const [text, setText] = useState('')
  const [result, setResult] = useState<QuickOrderResult>()
  const [message, setMessage] = useState<string>()
  const [busy, setBusy] = useState(false)
  const fileRef = useRef<HTMLInputElement>(null)

  function accept(r: QuickOrderResult) {
    setResult(r)
    replace(r.cart)
    if (r.errors.length === 0) setText('')
  }

  async function submitText(e: FormEvent) {
    e.preventDefault()
    if (countLines(text) === 0) { setMessage('Kod ve adet satırlarını yapıştırın.'); return }
    setBusy(true); setMessage(undefined)
    try { accept(await storeApi<QuickOrderResult>('/cart/quick', { method: 'POST', body: JSON.stringify({ text }) }, token)) }
    catch (err) { setMessage(err instanceof StoreApiError ? err.message : 'Sepete eklenemedi.') }
    finally { setBusy(false) }
  }

  async function submitFile(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    const invalid = validateUpload(file)
    if (invalid) { setMessage(invalid); return }
    setBusy(true); setMessage(undefined)
    try {
      const form = new FormData()
      form.append('file', file)
      accept(await storeApi<QuickOrderResult>('/cart/quick/upload', { method: 'POST', body: form }, token))
    } catch (err) { setMessage(err instanceof StoreApiError ? err.message : 'Dosya yüklenemedi.') }
    finally { setBusy(false) }
  }

  return (
    <section className="store-quick">
      <h1>Hızlı sipariş</h1>
      <p className="store-note">Ürün kodu (barkod) ve adedi yapıştırın ya da Excel dosyası yükleyin. Geçerli satırlar sepete eklenir; sorunlu satırlar aşağıda satır numarasıyla gösterilir.</p>

      <div className="store-quick__grid">
        <form className="ps-card store-quick__card" onSubmit={submitText} noValidate>
          <h2><ClipboardPaste size={18} aria-hidden /> Listeyi yapıştır</h2>
          <label className="field">
            <span className="field__label">Kod ve adet (her satıra bir ürün)</span>
            <textarea className="ps-input store-quick__text" rows={8} value={text} placeholder={example} onChange={(e) => setText(e.target.value)} spellCheck={false} />
            <span className="field__hint">Ayırıcı: noktalı virgül, sekme (Excel'den kopyala-yapıştır), virgül ya da boşluk. Adet yazılmazsa 1 sayılır.</span>
          </label>
          <button type="submit" className="ps-btn ps-btn--primary" disabled={busy}>{busy ? 'Ekleniyor…' : 'Sepete ekle'}</button>
        </form>

        <div className="ps-card store-quick__card">
          <h2><FileSpreadsheet size={18} aria-hidden /> Dosya yükle</h2>
          <p className="store-note">Excel (.xlsx) ya da .csv: ilk sütun ürün kodu, ikinci sütun adet. İlk satır başlık olabilir.</p>
          <input ref={fileRef} type="file" accept=".xlsx,.csv,.txt" hidden onChange={submitFile} />
          <button type="button" className="ps-btn" disabled={busy} onClick={() => fileRef.current?.click()}>Dosya seç</button>
        </div>
      </div>

      {message && <p role="alert" className="store-error">{message}</p>}

      {result && (
        <div className="ps-card store-quick__result" role="status">
          <p className={result.errors.length === 0 ? 'store-ok' : 'store-note'}>{summarize(result)}</p>
          {result.errors.length > 0 && (
            <>
              <table className="store-quick__errors">
                <thead><tr><th scope="col">Satır</th><th scope="col">Kod</th><th scope="col">Sorun</th></tr></thead>
                <tbody>{result.errors.map((e, i) => <tr key={`${e.row}-${i}`}><td>{e.row}</td><td>{e.code || '—'}</td><td>{e.message}</td></tr>)}</tbody>
              </table>
              <button type="button" className="ps-btn store-quick__copy" onClick={() => void navigator.clipboard?.writeText(errorsAsText(result.errors))}>Hataları kopyala</button>
            </>
          )}
          {result.added > 0 && <Link to="/sepet" className="ps-btn"><ShoppingCart size={16} aria-hidden /> Sepete git</Link>}
        </div>
      )}
    </section>
  )
}
