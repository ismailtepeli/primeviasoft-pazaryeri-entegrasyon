import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { DealerGroup, DealerListPage, PriceListRow, PricePreviewRow } from '../api/types'
import { Button, DataTable, Select, StatusBadge, Tabs, type Column } from '../components'
import PriceListEditor from './PriceListEditor'
import { formatMoney, listTarget, priceSourceLabel } from './priceListView'
import './PriceLists.css'

const sections = [{ value: 'lists', label: 'Listeler' }, { value: 'preview', label: 'Bayi fiyat önizleme' }]

function makeColumns(onEdit: (l: PriceListRow) => void, onDelete: (l: PriceListRow) => void): Column<PriceListRow>[] {
  return [
    {
      key: 'name', header: 'Liste',
      render: (l) => (
        <div className="price-cell">
          <button type="button" className="order-cell__link" aria-label={`${l.name} listesini düzenle`} onClick={() => onEdit(l)}>{l.name}</button>
          <span className="price-cell__meta">{l.currency} · {l.itemCount} ürün fiyatı</span>
        </div>
      ),
    },
    { key: 'target', header: 'Uygulanır', render: (l) => listTarget(l) },
    { key: 'discount', header: 'İskonto', render: (l) => (l.discountPercent ? `%${String(l.discountPercent).replace('.', ',')}` : '—') },
    { key: 'status', header: 'Durum', render: (l) => <StatusBadge variant={l.isActive ? 'success' : 'inactive'}>{l.isActive ? 'Aktif' : 'Pasif'}</StatusBadge> },
    { key: 'actions', header: 'İşlem', render: (l) => <Button onClick={() => onDelete(l)}>Sil</Button> },
  ]
}

const previewColumns: Column<PricePreviewRow>[] = [
  { key: 'name', header: 'Ürün', render: (r) => r.name },
  { key: 'price', header: 'Bayinin göreceği fiyat', render: (r) => <div className="price-cell">{formatMoney(r.display, r.currency)}<span className="price-cell__meta">{r.displayIncludesVat ? 'KDV dahil' : 'KDV hariç'}</span></div> },
  { key: 'source', header: 'Kaynak', render: (r) => priceSourceLabel[r.source] },
  { key: 'qty', header: 'Sipariş', render: (r) => `En az ${r.minOrderQuantity}, ${r.packSize}'lü` },
]

/** Fiyat listeleri: bayi grubuna ya da bayiye özel liste, ürün fiyatları, iskonto ve bayi fiyat önizlemesi. */
export default function PriceLists() {
  const [section, setSection] = useState('lists')
  const [lists, setLists] = useState<PriceListRow[]>([])
  const [groups, setGroups] = useState<DealerGroup[]>([])
  const [dealers, setDealers] = useState<{ id: string; name: string }[]>([])
  const [error, setError] = useState<string>()
  const [editor, setEditor] = useState<{ list?: PriceListRow }>()
  const [reloadKey, setReloadKey] = useState(0)
  const reload = useCallback(() => setReloadKey((k) => k + 1), [])

  const [dealerId, setDealerId] = useState('')
  const [preview, setPreview] = useState<PricePreviewRow[]>([])

  useEffect(() => {
    api<PriceListRow[]>('/price-lists').then((l) => { setLists(l); setError(undefined) }).catch((e: Error) => setError(e.message))
    api<DealerGroup[]>('/dealer-groups').then(setGroups).catch(() => { /* grup seçimi boş kalır */ })
    api<DealerListPage>('/dealers?status=active&pageSize=100').then((d) => setDealers(d.items.map((x) => ({ id: x.id, name: x.name })))).catch(() => { /* bayi seçimi boş kalır */ })
  }, [reloadKey])

  useEffect(() => {
    if (!dealerId) { setPreview([]); return }
    let current = true
    api<PricePreviewRow[]>(`/price-lists/preview?dealerId=${dealerId}`)
      .then((p) => { if (current) { setPreview(p); setError(undefined) } })
      .catch((e: Error) => { if (current) setError(e.message) })
    return () => { current = false }
  }, [dealerId, reloadKey])

  const columns = useMemo(() => makeColumns(
    (l) => setEditor({ list: l }),
    async (l) => {
      try { await api(`/price-lists/${l.id}`, { method: 'DELETE' }); setEditor(undefined); reload() }
      catch (e) { setError((e as Error).message) }
    },
  ), [reload])

  return (
    <div className="price-lists">
      <Tabs items={sections} value={section} onChange={setSection} />
      {error && <p role="alert" className="price-error">{error}</p>}
      {section === 'lists' && (
        <>
          <div className="price-toolbar">
            <p className="price-cell__meta">Öncelik: bayiye özel liste, bayi grubu listesi, genel fiyat. Kanal fiyat kurallarından bağımsızdır.</p>
            <Button variant="primary" onClick={() => setEditor({})}>Liste ekle</Button>
          </div>
          {editor && <PriceListEditor key={editor.list?.id ?? 'new'} list={editor.list} groups={groups} dealers={dealers} onClose={() => setEditor(undefined)} onChanged={reload} />}
          <DataTable columns={columns} rows={lists} rowKey={(l) => l.id} pageSize={20}
            emptyTitle="Fiyat listesi yok" emptyText="Bir bayi grubuna özel fiyat ya da iskonto tanımlamak için liste ekleyin." />
        </>
      )}
      {section === 'preview' && (
        <>
          <div className="price-form">
            <Select label="Bayi" value={dealerId} onChange={(e) => setDealerId(e.target.value)}>
              <option value="">Bayi seçin</option>
              {dealers.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </Select>
          </div>
          <DataTable columns={previewColumns} rows={preview} rowKey={(r) => r.productId} pageSize={50}
            emptyTitle={dealerId ? 'Ürün yok' : 'Bayi seçin'} emptyText="Seçilen bayinin sitede göreceği fiyatlar burada görünür (ilk 50 ürün)." />
        </>
      )}
    </div>
  )
}
