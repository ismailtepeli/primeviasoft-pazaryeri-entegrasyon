import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { DashboardSummary } from '../api/types'
import { KpiCard } from '../components'
import ChannelPanel from './ChannelPanel'
import { kpiCards } from './kpiCards'
import SyncErrorPanel from './SyncErrorPanel'
import './Overview.css'

export default function Overview() {
  const [summary, setSummary] = useState<DashboardSummary>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    api<DashboardSummary>('/dashboard/summary').then(setSummary).catch((e: Error) => setError(e.message))
  }, [])

  return (
    <div className="overview">
      {error && <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>}
      <div className="overview__kpis">
        {kpiCards(summary).map((c) => <KpiCard key={c.label} {...c} />)}
      </div>
      <ChannelPanel />
      <SyncErrorPanel />
    </div>
  )
}
