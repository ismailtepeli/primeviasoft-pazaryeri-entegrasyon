import { useState } from 'react'
import { Tabs } from '../components'
import PricingSection from './PricingSection'
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
      <PricingSection key={channel} channel={channel} />
    </div>
  )
}
