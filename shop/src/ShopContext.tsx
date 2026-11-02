import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { ShopApiError, shopApi } from './api'
import type { ShopCategory } from './shop'
import type { TenantRoute } from './tenant'

export interface ShopInfo { name: string }

/** Mağaza açılamıyorsa gösterilecek durum. */
export type ShopProblem = 'loading' | 'notfound' | { error: string }

type Phase = { kind: 'loading' } | { kind: 'ready'; info: ShopInfo } | { kind: 'notfound' } | { kind: 'error'; message: string }

interface ShopValue {
  tenant: TenantRoute
  info: ShopInfo
  /** Kategori ağacı; `categoriesLoaded` false iken liste henüz gelmemiştir. */
  categories: ShopCategory[]
  categoriesLoaded: boolean
  /** Bu mağazaya özel API çağrısı (kiracı slug'ı otomatik eklenir). */
  api: <T>(path: string) => Promise<T>
}

const Ctx = createContext<ShopValue | null>(null)

export function useShop(): ShopValue {
  const v = useContext(Ctx)
  if (!v) throw new Error('ShopProvider gerekli')
  return v
}

interface Props {
  tenant: TenantRoute
  children: ReactNode
  /** Mağaza yüklenirken, bulunamazken ya da hata verirken gösterilecek ekran. */
  renderState: (problem: ShopProblem, retry: () => void) => ReactNode
}

/** Mağazayı açarken kiracı bilgisini ve kategori ağacını yükler; mağaza yoksa (ya da B2C modülü kapalıysa) içeriği göstermez. */
export function ShopProvider({ tenant, children, renderState }: Props) {
  const [phase, setPhase] = useState<Phase>({ kind: 'loading' })
  const [categories, setCategories] = useState<ShopCategory[]>([])
  const [categoriesLoaded, setCategoriesLoaded] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const api = useCallback(<T,>(path: string) => shopApi<T>(tenant.slug, path), [tenant.slug])

  useEffect(() => {
    let current = true
    setPhase({ kind: 'loading' })
    setCategoriesLoaded(false)
    shopApi<ShopInfo>(tenant.slug, '/info')
      .then((info) => { if (current) setPhase({ kind: 'ready', info }) })
      .catch((e: unknown) => { if (current) setPhase(e instanceof ShopApiError && e.status === 404 ? { kind: 'notfound' } : { kind: 'error', message: (e as Error).message }) })
    shopApi<ShopCategory[]>(tenant.slug, '/categories')
      .then((c) => { if (current) setCategories(c) })
      .catch(() => { if (current) setCategories([]) })
      .finally(() => { if (current) setCategoriesLoaded(true) })
    return () => { current = false }
  }, [tenant.slug, attempt])

  const info = phase.kind === 'ready' ? phase.info : undefined
  useEffect(() => { if (info) document.title = (import.meta.env.DEV ? '[B2C] ' : '') + info.name }, [info])

  const retry = () => setAttempt((a) => a + 1)
  if (phase.kind === 'loading') return <>{renderState('loading', retry)}</>
  if (phase.kind === 'notfound') return <>{renderState('notfound', retry)}</>
  if (phase.kind === 'error') return <>{renderState({ error: phase.message }, retry)}</>
  return <Ctx.Provider value={{ tenant, info: phase.info, categories, categoriesLoaded, api }}>{children}</Ctx.Provider>
}
