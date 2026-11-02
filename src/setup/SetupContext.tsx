import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import type { SetupState } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { useModules } from '../modules/ModulesContext'

interface SetupValue {
  /** Henüz yüklenmediyse undefined. */
  setup: SetupState | undefined
  /** Seçilen kanalları uygular (modülleri açar/kapatır) ve kurulumu tamamlar; kuralı aşan istekte API'nin Türkçe mesajıyla hata fırlatır. */
  apply: (channels: string[]) => Promise<void>
}

const SetupContext = createContext<SetupValue | null>(null)

/** Satış kanalı kurulum durumu; ilk girişte sihirbazı göstermek ve Ayarlar > Satış kanalları için. */
export function SetupProvider({ children }: { children: ReactNode }) {
  const { authenticated } = useAuth()
  const { reload } = useModules()
  const [setup, setSetup] = useState<SetupState>()

  useEffect(() => {
    if (!authenticated) { setSetup(undefined); return }
    api<SetupState>('/setup').then(setSetup).catch(() => { /* durum gelmezse sihirbaz zorlanmaz */ })
  }, [authenticated])

  const apply = useCallback(async (channels: string[]) => {
    setSetup(await api<SetupState>('/setup/channels', { method: 'PUT', body: JSON.stringify({ channels }) }))
    await reload() // menü yeni modül durumuna göre güncellensin
  }, [reload])

  const value = useMemo<SetupValue>(() => ({ setup, apply }), [setup, apply])
  return <SetupContext.Provider value={value}>{children}</SetupContext.Provider>
}

export function useSetup(): SetupValue {
  const ctx = useContext(SetupContext)
  if (!ctx) throw new Error('useSetup, SetupProvider içinde kullanılmalı.')
  return ctx
}
