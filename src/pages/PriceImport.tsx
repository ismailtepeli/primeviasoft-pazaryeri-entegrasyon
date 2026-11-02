import { useRef, useState } from 'react'
import { api, download } from '../api/client'
import type { ProductPriceImportError, ProductPriceImportResult } from '../api/types'
import { Button, DataTable, type Column } from '../components'
import { priceImportSummary } from './priceEdit'
import './ChannelForm.css'

const columns: Column<ProductPriceImportError>[] = [
  { key: 'row', header: 'Satır', render: (e) => e.row },
  { key: 'barcode', header: 'Barkod', render: (e) => e.barcode || '—' },
  { key: 'message', header: 'Hata', render: (e) => e.message },
]

/** Mevcut fiyatlarla dolu Excel'i indirir, düzenlenmişini yükler; yalnızca barkod ve fiyat okunur. */
export default function PriceImport({ onClose, onImported }: { onClose: () => void; onImported: () => void }) {
  const input = useRef<HTMLInputElement>(null)
  const [fileName, setFileName] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const [result, setResult] = useState<ProductPriceImportResult>()

  async function upload() {
    const file = input.current?.files?.[0]
    if (!file) { setError('Önce bir Excel dosyası seçin.'); return }
    setBusy(true)
    setError(undefined)
    setResult(undefined)
    try {
      const body = new FormData()
      body.append('file', file)
      const r = await api<ProductPriceImportResult>('/products/price-import', { method: 'POST', body })
      setResult(r)
      if (r.updated > 0) onImported()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Yükleme başarısız.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="ps-card channel-form" style={{ maxWidth: 'none' }} aria-labelledby="price-import-title">
      <h2 id="price-import-title">Excel ile fiyat güncelle</h2>
      <p className="channel-form__note">
        Mevcut fiyatlarla dolu dosyayı indirin, yalnızca “Fiyat” sütununu değiştirip geri yükleyin. Barkod eşleşmeyen satırlar raporlanır; ürün eklenmez.
      </p>
      <div className="channel-form__actions" style={{ justifyContent: 'flex-start' }}>
        <Button onClick={() => download('/products/price-sheet', 'urun-fiyatlari.xlsx').catch((e: Error) => setError(e.message))}>Fiyat dosyasını indir</Button>
        <input ref={input} type="file" accept=".xlsx" aria-label="Excel dosyası seç"
          onChange={(e) => { setFileName(e.target.files?.[0]?.name); setResult(undefined); setError(undefined) }} />
      </div>

      {error && <p className="channel-form__error" role="alert">{error}</p>}
      {result && (
        <>
          <p className={result.errors.length ? 'channel-form__error' : 'channel-form__ok'} role="status">{priceImportSummary(result)}</p>
          {result.errors.length > 0 && <DataTable columns={columns} rows={result.errors} rowKey={(e) => `${e.row}`} pageSize={10} />}
        </>
      )}

      <div className="channel-form__actions">
        <Button onClick={onClose}>Kapat</Button>
        <Button onClick={upload} disabled={busy || !fileName}>{busy ? 'Yükleniyor…' : 'Yükle'}</Button>
      </div>
    </section>
  )
}
