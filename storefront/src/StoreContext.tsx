import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { StoreApiError, storeApi, tokenStore } from './api'
import type { TenantRoute } from './tenant'

export interface StoreInfo { name: string; available: boolean }

export interface DealerUserInfo { email: string; fullName: string; dealerName: string; role: string }

interface LoginResponse { accessToken: string; user: DealerUserInfo }

type Phase = { kind: 'loading' } | { kind: 'ready'; info: StoreInfo } | { kind: 'notfound' } | { kind: 'error'; message: string }

export type SiteState = 'loading' | 'notfound' | 'unavailable' | { error: string }

interface StoreValue {
  tenant: TenantRoute
  info: StoreInfo
  user: DealerUserInfo | null
  token: string | null
  login: (email: string, password: string, remember?: boolean) => Promise<void>
  logout: () => void
}

const Ctx = createContext<StoreValue | null>(null)

export function useStore(): StoreValue {
  const v = useContext(Ctx)
  if (!v) throw new Error('StoreProvider gerekli')
  return v
}

interface Props {
  tenant: TenantRoute
  children: ReactNode
  /** Site yüklenirken ya da kullanılamazken gösterilecek ekran. */
  renderState: (state: SiteState, retry: () => void) => ReactNode
}

/** Siteyi açarken kiracı bilgisini ve (varsa) kayıtlı bayi oturumunu yükler; site hizmet dışıysa içeriği göstermez. */
export function StoreProvider({ tenant, children, renderState }: Props) {
  const [phase, setPhase] = useState<Phase>({ kind: 'loading' })
  const [token, setToken] = useState<string | null>(() => tokenStore.get(tenant.slug))
  const [user, setUser] = useState<DealerUserInfo | null>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let current = true
    setPhase({ kind: 'loading' })
    storeApi<StoreInfo>(`/auth/info?slug=${encodeURIComponent(tenant.slug)}`)
      .then((info) => { if (current) setPhase({ kind: 'ready', info }) })
      .catch((e: unknown) => {
        if (!current) return
        setPhase(e instanceof StoreApiError && e.status === 404 ? { kind: 'notfound' } : { kind: 'error', message: (e as Error).message })
      })
    return () => { current = false }
  }, [tenant.slug, attempt])

  // Kayıtlı oturum hâlâ geçerli mi: sayfa yenilendiğinde token'dan kullanıcı bilgisini yeniden kurar.
  useEffect(() => {
    if (!token) { setUser(null); return }
    let current = true
    storeApi<{ email: string; name: string }>('/auth/me', {}, token)
      .then((me) => { if (current) setUser((u) => u ?? { email: me.email, fullName: me.name, dealerName: '', role: '' }) })
      .catch((e: unknown) => {
        if (current && e instanceof StoreApiError && e.status === 401) { tokenStore.clear(tenant.slug); setToken(null) }
      })
    return () => { current = false }
  }, [token, tenant.slug])

  const login = useCallback(async (email: string, password: string, remember = false) => {
    const r = await storeApi<LoginResponse>('/auth/login', { method: 'POST', body: JSON.stringify({ tenantSlug: tenant.slug, email, password }) })
    tokenStore.set(tenant.slug, r.accessToken, remember)
    setUser(r.user)
    setToken(r.accessToken)
  }, [tenant.slug])

  const logout = useCallback(() => { tokenStore.clear(tenant.slug); setToken(null); setUser(null) }, [tenant.slug])

  const info = phase.kind === 'ready' ? phase.info : null
  const value = useMemo<StoreValue | null>(() => (info ? { tenant, info, user, token, login, logout } : null), [tenant, info, user, token, login, logout])

  if (phase.kind === 'loading') return <>{renderState('loading', () => {})}</>
  if (phase.kind === 'notfound') return <>{renderState('notfound', () => {})}</>
  if (phase.kind === 'error') return <>{renderState({ error: phase.message }, () => setAttempt((a) => a + 1))}</>
  if (!value || !info!.available) return <>{renderState('unavailable', () => setAttempt((a) => a + 1))}</>
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
