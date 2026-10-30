import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { CategoryMappingList, CategoryMappingRow, ChannelCategory } from '../api/types'
import { Button, DataTable, StatusBadge, type Column } from '../components'
import CategoryPicker from './CategoryPicker'
import { blockingCount, mappingStatus } from './mappingStatus'
import './ChannelForm.css'

/** Kendi kategorilerinin seçili kanaldaki kategorilerle eşlemesi. */
export default function CategoryMappingSection({ channel }: { channel: string }) {
  const [data, setData] = useState<CategoryMappingList>()
  const [error, setError] = useState<string>()
  const [picking, setPicking] = useState<CategoryMappingRow>()
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()

  const load = useCallback(() => {
    api<CategoryMappingList>(`/mappings/categories?channel=${encodeURIComponent(channel)}`)
      .then((d) => { setData(d); setError(undefined) })
      .catch((e: Error) => setError(e.message))
  }, [channel])
  useEffect(() => { setData(undefined); setPicking(undefined); setMessage(undefined); load() }, [load])

  async function save(row: CategoryMappingRow, category: ChannelCategory) {
    try {
      await api('/mappings/categories', { method: 'PUT', body: JSON.stringify({ channel, categoryId: row.categoryId, externalCategoryId: category.externalId }) })
      setPicking(undefined)
      setMessage({ ok: true, text: `"${row.path}" → "${category.path}" eşlendi.` })
      load()
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Eşleme kaydedilemedi.' })
    }
  }

  async function remove(row: CategoryMappingRow) {
    try {
      await api(`/mappings/categories?channel=${encodeURIComponent(channel)}&categoryId=${row.categoryId}`, { method: 'DELETE' })
      setMessage({ ok: true, text: `"${row.path}" eşlemesi kaldırıldı.` })
      load()
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Eşleme kaldırılamadı.' })
    }
  }

  const columns: Column<CategoryMappingRow>[] = [
    { key: 'own', header: 'Senin kategorin', render: (r) => r.path },
    { key: 'count', header: 'Ürün', render: (r) => r.productCount },
    {
      key: 'channel', header: `${channel} kategorisi`,
      render: (r) => r.mapped
        ? <span className={r.mapped.isActive ? undefined : 'mapping-stale'}>{r.mapped.path}</span>
        : '—',
    },
    { key: 'status', header: 'Durum', render: (r) => { const s = mappingStatus(r); return <StatusBadge variant={s.variant}>{s.text}</StatusBadge> } },
    {
      key: 'actions', header: '',
      render: (r) => (
        <span className="category-actions">
          <Button onClick={() => setPicking(r)}>{r.mapped ? 'Değiştir' : 'Eşle'}</Button>
          {r.mapped && <Button onClick={() => remove(r)}>Kaldır</Button>}
        </span>
      ),
    },
  ]

  if (error) return <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>
  if (!data) return <p>Yükleniyor…</p>
  const blocking = blockingCount(data.rows)

  return (
    <section className="mapping-section" aria-labelledby="catmap-title">
      <header className="mapping-section__head">
        <h2 id="catmap-title">Kategori eşleme</h2>
        <span>{data.mappedCount} / {data.total} kategori eşlendi</span>
      </header>
      {blocking > 0 && (
        <p className="channel-form__error" role="alert">
          {blocking} kategorinin ürünleri var ama eşlemesi eksik ya da geçersiz; bu ürünler {channel}'a gönderilemez ve "Eksik" görünür.
        </p>
      )}
      {message && <p className={message.ok ? 'channel-form__ok' : 'channel-form__error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      {picking && <CategoryPicker channel={channel} title={`"${picking.path}" için ${channel} kategorisi seç`} onPick={(c) => save(picking, c)} onClose={() => setPicking(undefined)} />}
      <DataTable columns={columns} rows={data.rows} rowKey={(r) => r.categoryId} pageSize={15}
        emptyTitle="Henüz kategorin yok"
        emptyText="Ürün sayfasından ya da Excel yüklemesiyle kategori ekleyin." />
      <p className="channel-form__note">Kategorilerini <Link to="/urunler">Ürünler</Link> ekranından yönetirsin.</p>
    </section>
  )
}
