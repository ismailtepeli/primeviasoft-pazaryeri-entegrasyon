import { useState } from 'react'
import { Tabs } from '../components'
import ModuleSettings from './ModuleSettings'

const tabs = [{ value: 'modules', label: 'Modüller' }]

/** Ayarlar: şimdilik Modüller sekmesi; Hesap/Firma ayarları sonra eklenecek. */
export default function Settings() {
  const [tab, setTab] = useState('modules')
  return (
    <div className="settings">
      <Tabs items={tabs} value={tab} onChange={setTab} />
      {tab === 'modules' && <ModuleSettings />}
    </div>
  )
}
