import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { SyncErrorPage } from '../api/types'
import { timeAgo } from '../lib/timeAgo'
import './SyncErrorPanel.css'

const PREVIEW_COUNT = 5

export default function SyncErrorPanel() {
  const [page, setPage] = useState<SyncErrorPage>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    api<SyncErrorPage>(`/dashboard/sync-errors?take=${PREVIEW_COUNT}`)
      .then(setPage)
      .catch((e: Error) => setError(e.message))
  }, [])

  return (
    <section className="ps-card" aria-labelledby="sync-errors-title">
      <div className="sync-errors__head">
        <h2 id="sync-errors-title">Son senkron hataları</h2>
        <Link to="/senkron-hatalari">Tümünü gör</Link>
      </div>
      {error && <p className="sync-errors__error" role="alert">{error}</p>}
      {!error && !page && <p className="sync-errors__muted">Yükleniyor…</p>}
      {page && page.items.length === 0 && <p className="sync-errors__muted">Senkron hatası yok.</p>}
      {page && page.items.length > 0 && (
        <ul className="sync-errors__list">
          {page.items.map((e) => (
            <li key={e.id}>
              <Link to={e.target} className="sync-error">
                <span className="sync-error__message">{e.message}</span>
                <span className="sync-error__meta">
                  {e.channel}
                  {e.affectedCount > 0 && ` · ${e.affectedCount} ürün etkilendi`}
                  {` · ${timeAgo(e.occurredAt)}`}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
