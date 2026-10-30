import { useState } from 'react'
import { Tabs } from '../components'
import { useModules } from '../modules/ModulesContext'
import { isModuleEnabled } from '../modules/moduleState'
import B2BVisibility from './B2BVisibility'
import ModuleSettings from './ModuleSettings'


/** Ayarlar: şimdilik Modüller sekmesi; Hesap/Firma ayarları sonra eklenecek. */
export default function Settings() {
  const [tab, setTab] = useState('modules')
  const { enabled } = useModules()
  const tabs = [{ value: 'modules', label: 'Modüller' }, ...(isModuleEnabled('b2b-store', enabled) ? [{ value: 'b2b', label: 'B2B görünürlük' }] : [])]
  const active = tabs.some((t) => t.value === tab) ? tab : 'modules'
  return (
    <div className="settings">
      <Tabs items={tabs} value={active} onChange={setTab} />
      {active === 'modules' && <ModuleSettings />}
      {active === 'b2b' && <B2BVisibility />}
    </div>
  )
}
