import { useCallback, useEffect, useMemo, useRef, useState, type ChangeEvent } from 'react'
import { api } from '../api/client'
import type { DealerGroup, PriceImportResult, PriceListItemPage, PriceListItemRow, PriceListRow } from '../api/types'
import { Button, DataTable, Select, TextInput, type Column } from '../components'
import {
  PRICE_PAGE_SIZE, currencies, emptyPriceListForm, formFromList, formatMoney, parseCount, parseDecimal, priceImportSummary,
  toPriceListInput, validatePriceListForm, validateQuantity, type PriceListForm,
} from './priceListView'
import './PriceLists.css'

interface DealerOption { id: string; name: string }

interface Props {
  /** Düzenlenen liste; yoksa yeni liste eklenir. */
  list?: PriceListRow
  groups: DealerGroup[]
  dealers: DealerOption[]
  onClose: () => void
  onChanged: () => void
}

interface RowProps {
  row: PriceListItemRow
  currency: string
  listId: string
  onSaved: () => void
  onError: (message: string) => void
}

/** Tablo satırı: liste fiyatı, minimum ve koli adedi satır içinde düzenlenir. */
function ItemFields({ row, currency, listId, onSaved, onError }: RowProps) {
  const [price, setPrice] = useState(row.listPrice == null ? '' : String(row.listPrice).replace('.', ','))
  const [min, setMin] = useState(String(row.minOrderQuantity))
  const [pack, setPack] = useState(String(row.packSize))
  const [busy, setBusy] = useState(false)

  async function save() {
    const priceValue = parseDecimal(price)
    if (price.trim() !== '' && priceValue == null) { onError(`${row.name}: fiyat geçerli bir sayı olmalı.`); return }
    const qtyError = validateQuantity(min, pack)
    if (qtyError) { onError(`${row.name}: ${qtyError}`); return }
    setBusy(true)
    try {
      if (priceValue !== row.listPrice) await api(`/price-lists/${listId}/items/${row.productId}`, { method: 'PUT', body: JSON.stringify({ price: priceValue }) })
      if (parseCount(min) !== row.minOrderQuantity || parseCount(pack) !== row.packSize)
        await api(`/price-lists/products/${row.productId}/quantity`, { method: 'PUT', body: JSON.stringify({ minOrderQuantity: parseCount(min), packSize: parseCount(pack) }) })
      onSaved()
    } catch (e) { onError((e as Error).message) }
    finally { setBusy(false) }
  }

  const dirty = (parseDecimal(price) ?? null) !== row.listPrice || parseCount(min) !== row.minOrderQuantity || parseCount(pack) !== row.packSize
  return (
    <span className="price-fields">
      <input className="ps-input price-fields__input" inputMode="decimal" aria-label={`${row.name} liste fiyatı (${currency})`} placeholder="—" value={price} onChange={(e) => setPrice(e.target.value)} />
      <input className="ps-input price-fields__qty" inputMode="numeric" aria-label={`${row.name} minimum adet`} value={min} onChange={(e) => setMin(e.target.value)} />
      <input className="ps-input price-fields__qty" inputMode="numeric" aria-label={`${row.name} koli adedi`} value={pack} onChange={(e) => setPack(e.target.value)} />
      <Button onClick={save} disabled={busy || !dirty}>Kaydet</Button>
    </span>
  )
}

/** Fiyat listesi ekleme/düzenleme paneli; kayıtlı listede ürün fiyatları tablosu ve Excel ile toplu yükleme de vardır. */
export default function PriceListEditor({ list, groups, dealers, onClose, onChanged }: Props) {
  const [form, setForm] = useState<PriceListForm>(list ? formFromList(list) : emptyPriceListForm)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [saved, setSaved] = useState<PriceListRow | undefined>(list)
  const [saving, setSaving] = useState(false)

  const [items, setItems] = useState<PriceListItemPage>({ items: [], total: 0 })
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [onlyPriced, setOnlyPriced] = useState(false)
  const [page, setPage] = useState(1)
  const [reloadKey, setReloadKey] = useState(0)
  const [importResult, setImportResult] = useState<PriceImportResult>()
  const file = useRef<HTMLInputElement>(null)
  const reload = useCallback(() => setReloadKey((k) => k + 1), [])
  const listId = saved?.id

  useEffect(() => {
    const t = setTimeout(() => { setSearch(input); setPage(1) }, 300)
    return () => clearTimeout(t)
  }, [input])

  useEffect(() => {
    if (!listId) return
    let current = true
    const qs = new URLSearchParams({ page: String(page), pageSize: String(PRICE_PAGE_SIZE), onlyPriced: String(onlyPriced) })
    if (search.trim()) qs.set('q', search.trim())
    api<PriceListItemPage>(`/price-lists/${listId}/items?${qs}`)
      .then((d) => { if (current) setItems(d) })
      .catch((e: Error) => { if (current) setMessage({ ok: false, text: e.message }) })
    return () => { current = false }
  }, [listId, page, search, onlyPriced, reloadKey])

  const set = (key: keyof PriceListForm) => (e: ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setForm((f) => ({ ...f, [key]: e.target.value }))
  const flag = (key: 'pricesIncludeVat' | 'showVatIncluded' | 'isActive') => (e: ChangeEvent<HTMLInputElement>) => setForm((f) => ({ ...f, [key]: e.target.checked }))

  async function save() {
    const found = validatePriceListForm(form)
    setErrors(found)
    if (Object.keys(found).length > 0) return
    setSaving(true); setMessage(undefined)
    try {
      const body = JSON.stringify(toPriceListInput(form))
      const result = listId
        ? await api<PriceListRow>(`/price-lists/${listId}`, { method: 'PUT', body })
        : await api<PriceListRow>('/price-lists', { method: 'POST', body })
      setSaved(result)
      setMessage({ ok: true, text: listId ? 'Liste kaydedildi.' : 'Liste eklendi; şimdi ürün fiyatlarını girebilirsiniz.' })
      onChanged()
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
    finally { setSaving(false) }
  }

  async function upload() {
    const f = file.current?.files?.[0]
    if (!f || !listId) { setMessage({ ok: false, text: 'Önce bir Excel dosyası seçin.' }); return }
    try {
      const body = new FormData()
      body.append('file', f)
      const r = await api<PriceImportResult>(`/price-lists/${listId}/import`, { method: 'POST', body })
      setImportResult(r); setMessage(undefined)
      if (r.created + r.updated > 0) { reload(); onChanged() }
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
  }

  const currency = saved?.currency ?? form.currency
  const columns = useMemo<Column<PriceListItemRow>[]>(() => [
    { key: 'name', header: 'Ürün', render: (r) => <div className="price-cell"><strong>{r.name}</strong><span className="price-cell__meta">{r.barcode}</span></div> },
    { key: 'general', header: 'Genel fiyat', render: (r) => formatMoney(r.generalPrice) },
    {
      key: 'edit', header: `Liste fiyatı · Min · Koli`,
      render: (r) => listId && <ItemFields key={`${r.productId}-${r.listPrice}-${r.minOrderQuantity}-${r.packSize}`} row={r} currency={currency} listId={listId}
        onSaved={() => { setMessage(undefined); reload(); onChanged() }} onError={(text) => setMessage({ ok: false, text })} />,
    },
  ], [listId, currency, reload, onChanged])

  return (
    <section className="ps-card price-editor" aria-label={list ? 'Fiyat listesini düzenle' : 'Yeni fiyat listesi'}>
      <header className="price-editor__head">
        <h2>{saved ? saved.name : 'Yeni fiyat listesi'}</h2>
        <Button onClick={onClose}>Kapat</Button>
      </header>
      <div className="price-form">
        <TextInput label="Liste adı" value={form.name} onChange={set('name')} error={errors.name} />
        <Select label="Para birimi" value={form.currency} onChange={set('currency')}>
          {currencies.map((c) => <option key={c} value={c}>{c}</option>)}
        </Select>
        <Select label="Uygulanacak" value={form.targetKind} onChange={(e) => setForm((f) => ({ ...f, targetKind: e.target.value as PriceListForm['targetKind'], targetId: '' }))}>
          <option value="group">Bayi grubuna</option>
          <option value="dealer">Tek bir bayiye</option>
          <option value="none">Henüz bağlama</option>
        </Select>
        {form.targetKind === 'group' && (
          <Select label="Bayi grubu" value={form.targetId} onChange={set('targetId')} error={errors.targetId}>
            <option value="">Seçin</option>
            {groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
          </Select>
        )}
        {form.targetKind === 'dealer' && (
          <Select label="Bayi" value={form.targetId} onChange={set('targetId')} error={errors.targetId}>
            <option value="">Seçin</option>
            {dealers.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </Select>
        )}
        <TextInput label="İskonto (%)" hint="Listede fiyatı olmayan ürünlere genel fiyattan düşülür" inputMode="decimal" value={form.discount} onChange={set('discount')} error={errors.discount} />
      </div>
      <div className="price-checks">
        <label><input type="checkbox" checked={form.pricesIncludeVat} onChange={flag('pricesIncludeVat')} /> Girilen fiyatlar KDV dahil</label>
        <label><input type="checkbox" checked={form.showVatIncluded} onChange={flag('showVatIncluded')} /> Bayi sitesinde fiyatları KDV dahil göster</label>
        <label><input type="checkbox" checked={form.isActive} onChange={flag('isActive')} /> Liste aktif</label>
      </div>
      {message && <p className={message.ok ? 'price-ok' : 'price-error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      <div className="price-editor__actions"><Button onClick={save} disabled={saving}>{saving ? 'Kaydediliyor…' : 'Kaydet'}</Button></div>

      {listId && (
        <div className="price-items">
          <h3>Ürün fiyatları</h3>
          <p className="price-cell__meta">Fiyatı boş bırakılan ürün iskontoya ya da genel fiyata düşer. Minimum ve koli adedi ürüne aittir; tüm listeler için geçerlidir.</p>
          <div className="price-toolbar">
            <input className="ps-input price-toolbar__search" type="search" placeholder="Ürün adı ya da barkod ara" aria-label="Ürün ara" value={input} onChange={(e) => setInput(e.target.value)} />
            <label><input type="checkbox" checked={onlyPriced} onChange={(e) => { setOnlyPriced(e.target.checked); setPage(1) }} /> Yalnızca fiyatlılar</label>
          </div>
          <div className="price-import">
            <input ref={file} type="file" accept=".xlsx" aria-label="Excel dosyası seç" onChange={() => setImportResult(undefined)} />
            <Button onClick={upload}>Excel ile yükle</Button>
            <span className="price-cell__meta">Başlıklar: Barkod, Fiyat; isteğe bağlı Minimum, Koli.</span>
          </div>
          {importResult && (
            <>
              <p className={importResult.errors.length ? 'price-error' : 'price-ok'} role="status">{priceImportSummary(importResult)}</p>
              {importResult.errors.length > 0 && (
                <ul className="price-errors">
                  {importResult.errors.slice(0, 20).map((e) => <li key={`${e.row}-${e.message}`}>Satır {e.row}: {e.message}</li>)}
                  {importResult.errors.length > 20 && <li>…ve {importResult.errors.length - 20} satır daha.</li>}
                </ul>
              )}
            </>
          )}
          <DataTable columns={columns} rows={items.items} rowKey={(r) => r.productId} pageSize={PRICE_PAGE_SIZE}
            serverPaging={{ page: page - 1, total: items.total, onPageChange: (p) => setPage(p + 1) }}
            emptyTitle="Ürün yok" emptyText="Aramaya uyan ürün bulunamadı." />
        </div>
      )}
    </section>
  )
}
