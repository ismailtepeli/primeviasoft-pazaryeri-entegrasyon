import { useState } from 'react'
import { Tabs } from '../components'
import { useModules } from '../modules/ModulesContext'
import { isModuleEnabled } from '../modules/moduleState'
import B2BVisibility from './B2BVisibility'
import B2CVisibility from './B2CVisibility'
import ModuleSettings from './ModuleSettings'
import SalesChannelSettings from '../setup/SalesChannelSettings'


/** Ayarlar: Satış kanalları ve Modüller sekmeleri; Hesap/Firma ayarları sonra eklenecek. */
export default function Settings() {
  const [tab, setTab] = useState('channels')
  const { enabled } = useModules()
  const tabs = [{ value: 'channels', label: 'Satış kanalları' }, { value: 'modules', label: 'Modüller' }, ...(isModuleEnabled('b2b-store', enabled) ? [{ value: 'b2b', label: 'B2B görünürlük' }] : []), ...(enabled !== null && enabled.has('b2c-store') ? [{ value: 'b2c', label: 'B2C görünürlük' }] : [])]
  const active = tabs.some((t) => t.value === tab) ? tab : 'channels'
  return (
    <div className="settings">
      <Tabs items={tabs} value={active} onChange={setTab} />
      {active === 'channels' && <SalesChannelSettings />}
      {active === 'modules' && <ModuleSettings />}
      {active === 'b2b' && <B2BVisibility />}
      {active === 'b2c' && <B2CVisibility />}
    </div>
  )
}
