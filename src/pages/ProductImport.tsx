import { useRef, useState } from 'react'
import { api, download, saveBlob } from '../api/client'
import type { ProductImportResult } from '../api/types'
import { Button, DataTable, type Column } from '../components'
import type { ProductImportError } from '../api/types'
import { errorsToCsv, importSummary } from './importReport'
import './ChannelForm.css'

const columns: Column<ProductImportError>[] = [
  { key: 'row', header: 'Satır', render: (e) => e.row },
  { key: 'barcode', header: 'Barkod', render: (e) => e.barcode || '—' },
  { key: 'message', header: 'Hata', render: (e) => e.message },
]

export default function ProductImport({ onClose, onImported }: { onClose: () => void; onImported: () => void }) {
  const input = useRef<HTMLInputElement>(null)
  const [fileName, setFileName] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const [result, setResult] = useState<ProductImportResult>()

  async function upload() {
    const file = input.current?.files?.[0]
    if (!file) { setError('Önce bir Excel dosyası seçin.'); return }
    setBusy(true)
    setError(undefined)
    setResult(undefined)
    try {
      const body = new FormData()
      body.append('file', file)
      const r = await api<ProductImportResult>('/products/import', { method: 'POST', body })
      setResult(r)
      if (r.created + r.updated > 0) onImported()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Yükleme başarısız.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="ps-card channel-form" style={{ maxWidth: 'none' }} aria-labelledby="import-title">
      <h2 id="import-title">Excel ile toplu ürün yükle</h2>
      <p className="channel-form__note">
        Şablonu indirip doldurun. Aynı ürün ana koduna sahip satırlar tek ürünün varyantı olur; kayıtlı barkodlar güncellenir.
      </p>
      <div className="channel-form__actions" style={{ justifyContent: 'flex-start' }}>
        <Button onClick={() => download('/products/template', 'urun-sablonu.xlsx').catch((e: Error) => setError(e.message))}>Şablonu indir</Button>
        <input ref={input} type="file" accept=".xlsx" aria-label="Excel dosyası seç"
          onChange={(e) => { setFileName(e.target.files?.[0]?.name); setResult(undefined); setError(undefined) }} />
      </div>

      {error && <p className="channel-form__error" role="alert">{error}</p>}
      {result && (
        <>
          <p className={result.errors.length ? 'channel-form__error' : 'channel-form__ok'} role="status">{importSummary(result)}</p>
          {result.errors.length > 0 && (
            <>
              <DataTable columns={columns} rows={result.errors} rowKey={(e) => `${e.row}`} pageSize={10} />
              <div>
                <Button onClick={() => saveBlob(new Blob([errorsToCsv(result.errors)], { type: 'text/csv;charset=utf-8' }), 'urun-yukleme-hatalari.csv')}>
                  Hata raporunu indir
                </Button>
              </div>
            </>
          )}
        </>
      )}

      <div className="channel-form__actions">
        <Button onClick={onClose}>Kapat</Button>
        <Button onClick={upload} disabled={busy || !fileName}>{busy ? 'Yükleniyor…' : 'Yükle'}</Button>
      </div>
    </section>
  )
}
