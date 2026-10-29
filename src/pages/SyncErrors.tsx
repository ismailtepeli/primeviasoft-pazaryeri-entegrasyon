import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { SyncError, SyncErrorPage } from '../api/types'
import { Button, DataTable, type Column } from '../components'
import { timeAgo } from '../lib/timeAgo'

const PAGE_SIZE = 200

const columns: Column<SyncError>[] = [
  { key: 'message', header: 'Hata', render: (e) => <Link to={e.target}>{e.message}</Link> },
  { key: 'channel', header: 'Kanal', render: (e) => e.channel },
  { key: 'affected', header: 'Etkilenen ürün', render: (e) => (e.affectedCount > 0 ? e.affectedCount : '—') },
  { key: 'at', header: 'Zaman', render: (e) => timeAgo(e.occurredAt) },
]

export default function SyncErrors() {
  const [items, setItems] = useState<SyncError[]>([])
  const [total, setTotal] = useState(0)
  const [error, setError] = useState<string>()
  const [loading, setLoading] = useState(true)

  function load(skip: number) {
    setLoading(true)
    api<SyncErrorPage>(`/dashboard/sync-errors?skip=${skip}&take=${PAGE_SIZE}`)
      .then((p) => {
        setItems((prev) => (skip === 0 ? p.items : [...prev, ...p.items]))
        setTotal(p.total)
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }

  useEffect(() => load(0), [])

  if (error) return <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-4)' }}>
      <DataTable columns={columns} rows={items} rowKey={(e) => e.id} emptyTitle={loading ? 'Yükleniyor…' : 'Senkron hatası yok'} emptyText="" />
      {items.length < total && (
        <Button onClick={() => load(items.length)} disabled={loading}>Daha fazla yükle ({total - items.length} kayıt daha)</Button>
      )}
    </div>
  )
}
