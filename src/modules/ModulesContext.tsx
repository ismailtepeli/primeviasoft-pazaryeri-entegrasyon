import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import type { ModuleStatus } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { enabledSet } from './moduleState'

interface ModulesValue {
  /** Henüz yüklenmediyse undefined. */
  modules: ModuleStatus[] | undefined
  /** Açık modüller; yüklenmediyse null. */
  enabled: Set<string> | null
  /** Modülü açar/kapatır; kurallara uymuyorsa API'nin Türkçe mesajıyla hata fırlatır. */
  setEnabled: (id: string, enabled: boolean) => Promise<void>
  reload: () => Promise<void>
}

const ModulesContext = createContext<ModulesValue | null>(null)

export function ModulesProvider({ children }: { children: ReactNode }) {
  const { authenticated } = useAuth()
  const [modules, setModules] = useState<ModuleStatus[]>()

  const reload = useCallback(async () => { setModules(await api<ModuleStatus[]>('/modules')) }, [])

  useEffect(() => {
    if (!authenticated) { setModules(undefined); return }
    reload().catch(() => { /* liste gelmezse menü tam görünür; ekranlar kendi hatasını gösterir */ })
  }, [authenticated, reload])

  const setEnabled = useCallback(async (id: string, enabled: boolean) => {
    setModules(await api<ModuleStatus[]>(`/modules/${id}`, { method: 'PUT', body: JSON.stringify({ enabled }) }))
  }, [])

  const value = useMemo<ModulesValue>(() => ({ modules, enabled: enabledSet(modules), setEnabled, reload }), [modules, setEnabled, reload])
  return <ModulesContext.Provider value={value}>{children}</ModulesContext.Provider>
}

export function useModules(): ModulesValue {
  const ctx = useContext(ModulesContext)
  if (!ctx) throw new Error('useModules, ModulesProvider içinde kullanılmalı.')
  return ctx
}
