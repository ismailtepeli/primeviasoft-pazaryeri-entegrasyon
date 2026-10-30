import type { ModuleStatus } from '../api/types'
import type { RouteDef } from '../routes'

/** Açık modüllerin kimlik kümesi; liste henüz yüklenmediyse null (her şey görünür, menü zıplamasın). */
export function enabledSet(modules: ModuleStatus[] | undefined): Set<string> | null {
  return modules ? new Set(modules.filter((m) => m.enabled).map((m) => m.id)) : null
}

/** Rota bir modüle bağlı değilse (çekirdek) ya da modülü açıksa görünür. */
export function isRouteEnabled(route: Pick<RouteDef, 'module'>, enabled: Set<string> | null): boolean {
  return !route.module || enabled === null || enabled.has(route.module)
}

/** Modül açık mı; liste yüklenmediyse açık sayılır (arayüz zıplamasın). */
export function isModuleEnabled(id: string, enabled: Set<string> | null): boolean {
  return enabled === null || enabled.has(id)
}

/** Verilen modüllerden en az biri açık mı. */
export function anyModuleEnabled(ids: string[], enabled: Set<string> | null): boolean {
  return ids.some((id) => isModuleEnabled(id, enabled))
}

/** Menüde gösterilecek rotalar: yalnızca açık modüllerinkiler. */
export function visibleRoutes(routes: RouteDef[], enabled: Set<string> | null): RouteDef[] {
  return routes.filter((r) => isRouteEnabled(r, enabled))
}

/** Modülü açmak için önce açılması gereken modüller (adlarıyla). */
export function missingDependencies(module: ModuleStatus, modules: ModuleStatus[]): ModuleStatus[] {
  return module.dependsOn.map((id) => modules.find((m) => m.id === id)).filter((m): m is ModuleStatus => !!m && !m.enabled)
}

/** Modülü kapatmadan önce kapatılması gereken, ona bağlı açık modüller. */
export function enabledDependents(module: ModuleStatus, modules: ModuleStatus[]): ModuleStatus[] {
  return modules.filter((m) => m.enabled && m.dependsOn.includes(module.id))
}

/** Anahtarın neden kullanılamadığı; kullanılabiliyorsa null. */
export function toggleBlockReason(module: ModuleStatus, modules: ModuleStatus[]): string | null {
  if (module.core) return 'Her zaman açık'
  if (module.enabled) {
    const dependents = enabledDependents(module, modules)
    return dependents.length > 0 ? `Önce kapatın: ${dependents.map((m) => m.name).join(', ')}` : null
  }
  const missing = missingDependencies(module, modules)
  return missing.length > 0 ? `Önce açın: ${missing.map((m) => m.name).join(', ')}` : null
}
