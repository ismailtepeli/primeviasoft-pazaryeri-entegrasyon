import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { DealerGroup, DealerListPage, DealerRow } from '../api/types'
import { Button, DataTable, StatusBadge, Tabs, type Column } from '../components'
import DealerEditor from './DealerEditor'
import DealerGroups from './DealerGroups'
import { DEALER_PAGE_SIZE, dealerActions, dealersQuery, dealerStatusBadge, dealerTabs, emptyDealerCounts, type DealerTab } from './dealerView'
import './Dealers.css'

const sections = [{ value: 'dealers', label: 'Bayiler' }, { value: 'groups', label: 'Bayi grupları' }]

function makeColumns(onEdit: (d: DealerRow) => void, onAction: (d: DealerRow, action: 'approve' | 'deactivate' | 'activate') => void): Column<DealerRow>[] {
  return [
    {
      key: 'name', header: 'Firma',
      render: (d) => (
        <div className="dealer-cell">
          <button type="button" className="order-cell__link" aria-label={`${d.name} bayisini düzenle`} onClick={() => onEdit(d)}>{d.name}</button>
          <span className="dealer-cell__meta">VN {d.taxNumber}{d.city ? ` · ${d.city}` : ''}</span>
        </div>
      ),
    },
    { key: 'contact', header: 'Yetkili', render: (d) => <div className="dealer-cell">{d.contactName || '—'}<span className="dealer-cell__meta">{d.email}</span></div> },
    { key: 'group', header: 'Grup', render: (d) => d.groupName ?? '—' },
    { key: 'status', header: 'Durum', render: (d) => { const b = dealerStatusBadge[d.status]; return <StatusBadge variant={b.variant}>{b.text}</StatusBadge> } },
    {
      key: 'actions', header: 'İşlem',
      render: (d) => (
        <span className="dealer-actions">
          {dealerActions(d.status).map((a) => <Button key={a.action} onClick={() => onAction(d, a.action)}>{a.label}</Button>)}
        </span>
      ),
    },
  ]
}

/** Bayi yönetimi: durum sekmeleri (başvurular, aktif, pasif), arama, bayi ekle/düzenle ve bayi grupları. */
export default function Dealers() {
  const [section, setSection] = useState('dealers')
  const [tab, setTab] = useState<DealerTab>('pending')
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<DealerListPage>({ items: [], total: 0, counts: emptyDealerCounts })
  const [groups, setGroups] = useState<DealerGroup[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()
  const [editor, setEditor] = useState<{ id?: string }>()
  const [reloadKey, setReloadKey] = useState(0)
  const reload = useCallback(() => setReloadKey((k) => k + 1), [])

  useEffect(() => {
    const t = setTimeout(() => { setSearch(input); setPage(1) }, 300)
    return () => clearTimeout(t)
  }, [input])

  useEffect(() => {
    let current = true
    setLoading(true)
    api<DealerListPage>(`/dealers?${dealersQuery(tab, search, page)}`)
      .then((d) => { if (current) { setData(d); setError(undefined) } })
      .catch((e: Error) => { if (current) setError(e.message) })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [tab, search, page, reloadKey])

  useEffect(() => {
    api<DealerGroup[]>('/dealer-groups').then(setGroups).catch(() => { /* grup listesi gelmezse form grupsuz çalışır */ })
  }, [reloadKey])

  const columns = useMemo(() => makeColumns(
    (d) => setEditor({ id: d.id }),
    async (d, action) => {
      try { await api(`/dealers/${d.id}/${action}`, { method: 'POST' }); setError(undefined); reload() }
      catch (e) { setError((e as Error).message) }
    },
  ), [reload])

  return (
    <div className="dealers">
      <Tabs items={sections} value={section} onChange={setSection} />
      {section === 'groups' && <DealerGroups groups={groups} onChanged={reload} />}
      {section === 'dealers' && (
        <>
          <div className="dealers__toolbar">
            <input className="ps-input dealers__search" type="search" placeholder="Firma, vergi no, e-posta ya da yetkili ara" aria-label="Bayi ara"
              value={input} onChange={(e) => setInput(e.target.value)} />
            <Button variant="primary" onClick={() => setEditor({})}>Bayi ekle</Button>
          </div>
          <Tabs items={dealerTabs(data.counts)} value={tab} onChange={(v) => { setTab(v as DealerTab); setPage(1) }} />
          {editor && <DealerEditor key={editor.id ?? 'new'} dealerId={editor.id} groups={groups} onClose={() => setEditor(undefined)} onChanged={reload} />}
          {error && <p role="alert" className="dealer-error">{error}</p>}
          <DataTable columns={columns} rows={data.items} rowKey={(d) => d.id} pageSize={DEALER_PAGE_SIZE}
            serverPaging={{ page: page - 1, total: data.total, onPageChange: (p) => setPage(p + 1) }}
            emptyTitle={loading ? 'Yükleniyor…' : tab === 'pending' ? 'Bekleyen başvuru yok' : 'Bayi yok'}
            emptyText={loading ? '' : 'Bayi sitesinden gelen başvurular burada onay bekler; bayiyi kendiniz de ekleyebilirsiniz.'} />
        </>
      )}
    </div>
  )
}
