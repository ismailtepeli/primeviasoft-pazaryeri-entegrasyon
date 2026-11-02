import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { ShopApiError, shopApi } from './api'
import type { CustomerProfile, CustomerSession } from './account'
import { useShop } from './ShopContext'
import { tokenStore } from './tokenStore'

interface CustomerValue {
  token: string | null
  profile: CustomerProfile | null
  /** Kayıtlı oturum varsa profil yüklenene kadar false; giriş durumu belli olana dek korumalı sayfalar yönlendirmez. */
  ready: boolean
  /** Google ile giriş açıksa herkese açık istemci kimliği; kapalıysa null (düğme gösterilmez). */
  googleClientId: string | null
  login: (email: string, password: string, remember: boolean) => Promise<void>
  register: (input: { fullName: string; email: string; password: string; phone: string; acceptTerms: boolean; marketingConsent: boolean }, remember: boolean) => Promise<void>
  googleLogin: (idToken: string, acceptTerms: boolean, marketingConsent: boolean, remember: boolean) => Promise<void>
  logout: () => void
  setProfile: (p: CustomerProfile) => void
  /** Oturumlu API çağrısı (/me/...); 401 gelirse oturum kapatılır. */
  api: <T>(path: string, init?: RequestInit) => Promise<T>
}

const Ctx = createContext<CustomerValue | null>(null)

export function useCustomer(): CustomerValue {
  const v = useContext(Ctx)
  if (!v) throw new Error('CustomerProvider gerekli')
  return v
}

export function CustomerProvider({ children }: { children: ReactNode }) {
  const { tenant } = useShop()
  const slug = tenant.slug
  const [token, setToken] = useState<string | null>(() => tokenStore.get(slug))
  const [profile, setProfile] = useState<CustomerProfile | null>(null)
  const [ready, setReady] = useState(() => tokenStore.get(slug) === null)
  const [googleClientId, setGoogleClientId] = useState<string | null>(null)

  const logout = useCallback(() => { tokenStore.clear(slug); setToken(null); setProfile(null); setReady(true) }, [slug])

  useEffect(() => {
    let current = true
    shopApi<{ clientId: string | null }>(slug, '/auth/config').then((c) => { if (current) setGoogleClientId(c.clientId) }).catch(() => { if (current) setGoogleClientId(null) })
    return () => { current = false }
  }, [slug])

  // Kayıtlı oturumun hâlâ geçerli olduğunu doğrular; sayfa yenilenince profili yeniden kurar.
  useEffect(() => {
    if (!token) { setProfile(null); setReady(true); return }
    let current = true
    shopApi<CustomerProfile>(slug, '/me', {}, token)
      .then((p) => { if (current) setProfile(p) })
      .catch((e: unknown) => { if (current && e instanceof ShopApiError && (e.status === 401 || e.status === 404)) logout() })
      .finally(() => { if (current) setReady(true) })
    return () => { current = false }
  }, [token, slug, logout])

  const start = useCallback((session: CustomerSession, remember: boolean) => {
    tokenStore.set(slug, session.accessToken, remember)
    setProfile(session.profile)
    setReady(true)
    setToken(session.accessToken)
  }, [slug])

  const api = useCallback(async <T,>(path: string, init: RequestInit = {}) => {
    try { return await shopApi<T>(slug, path, init, token) }
    catch (e) { if (e instanceof ShopApiError && e.status === 401) logout(); throw e }
  }, [slug, token, logout])

  const value = useMemo<CustomerValue>(() => ({
    token, profile, ready, googleClientId,
    login: async (email, password, remember) => start(await shopApi<CustomerSession>(slug, '/auth/login', { method: 'POST', body: JSON.stringify({ slug, email, password }) }), remember),
    register: async (input, remember) => start(await shopApi<CustomerSession>(slug, '/auth/register', { method: 'POST', body: JSON.stringify({ slug, ...input }) }), remember),
    googleLogin: async (idToken, acceptTerms, marketingConsent, remember) =>
      start(await shopApi<CustomerSession>(slug, '/auth/google', { method: 'POST', body: JSON.stringify({ slug, idToken, acceptTerms, marketingConsent }) }), remember),
    logout, setProfile, api,
  }), [token, profile, ready, googleClientId, slug, start, logout, api])

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
