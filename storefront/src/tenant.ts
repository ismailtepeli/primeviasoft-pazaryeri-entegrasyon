export interface TenantRoute {
  slug: string
  /** Yönlendirici (router) taban yolu: alt alan adında boş, yol önekinde "/t/<slug>". */
  basePath: string
}

const LABEL = /^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/

/**
 * Kiracıyı adresten çözer. Öncelik: alt alan adı (firma.b2b.site, baseDomain verilmişse), sonra geliştirme/yedek
 * olarak yol öneki (/t/firma). İkisi de yoksa null. Özel alan adı desteği sonradan eklenecek.
 */
export function resolveTenant(hostname: string, pathname: string, baseDomain?: string): TenantRoute | null {
  const host = hostname.trim().toLowerCase()
  const base = baseDomain?.trim().toLowerCase().replace(/^\./, '')
  if (base && host.endsWith(`.${base}`)) {
    const label = host.slice(0, -(base.length + 1))
    if (LABEL.test(label) && label !== 'www') return { slug: label, basePath: '' }
  }
  const m = /^\/t\/([a-zA-Z0-9-]+)(?:\/|$)/.exec(pathname)
  if (m && LABEL.test(m[1].toLowerCase())) return { slug: m[1].toLowerCase(), basePath: `/t/${m[1].toLowerCase()}` }
  return null
}
