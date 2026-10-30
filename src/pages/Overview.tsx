import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { DashboardSummary } from '../api/types'
import { KpiCard } from '../components'
import { useModules } from '../modules/ModulesContext'
import { anyModuleEnabled, isModuleEnabled } from '../modules/moduleState'
import ChannelPanel from './ChannelPanel'
import { kpiCards } from './kpiCards'
import SyncErrorPanel from './SyncErrorPanel'
import './Overview.css'

export default function Overview() {
  const { enabled } = useModules()
  const [summary, setSummary] = useState<DashboardSummary>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    api<DashboardSummary>('/dashboard/summary').then(setSummary).catch((e: Error) => setError(e.message))
  }, [])

  return (
    <div className="overview">
      {error && <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>}
      <div className="overview__kpis">
        {kpiCards(summary, enabled).map(({ anyOf: _anyOf, ...c }) => <KpiCard key={c.label} {...c} />)}
      </div>
      {isModuleEnabled('marketplace', enabled) && <ChannelPanel />}
      {anyModuleEnabled(['marketplace', 'orders'], enabled) && <SyncErrorPanel />}
    </div>
  )
}
