import { useState } from 'react'
import { Tabs } from '../components'
import PricingSection from './PricingSection'
import StockMovementsSection from './StockMovementsSection'
import StockSection from './StockSection'
import SyncStatusCard from './SyncStatusCard'
import './StockPrice.css'

const CHANNEL_TABS = [
  { value: 'Trendyol', label: 'Trendyol' },
  { value: 'Hepsiburada', label: 'Hepsiburada' },
  { value: 'N11', label: 'N11' },
  { value: 'Amazon', label: 'Amazon' },
]

export default function StockPrice() {
  const [channel, setChannel] = useState('Trendyol')
  return (
    <div className="stock-price">
      <Tabs items={CHANNEL_TABS} value={channel} onChange={setChannel} />
      <SyncStatusCard key={`sync-${channel}`} channel={channel} />
      <StockSection key={`stock-${channel}`} channel={channel} />
      <PricingSection key={channel} channel={channel} />
      <StockMovementsSection />
    </div>
  )
}
