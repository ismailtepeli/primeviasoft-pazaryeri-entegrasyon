import { useEffect } from 'react'
import { formatMoney } from './shop'

const TR: Record<string, string> = { ç: 'c', ğ: 'g', ı: 'i', i̇: 'i', ö: 'o', ş: 's', ü: 'u', â: 'a', î: 'i', û: 'u' }

/** Okunur adres parçası: küçük harf, Türkçe karakterler sadeleşir, harf/rakam dışı tireye döner, en fazla 80 karakter. */
export function slugify(text: string): string {
  const mapped = text.toLocaleLowerCase('tr-TR').replace(/[çğıöşüâîû]|i̇/g, (c) => TR[c] ?? c)
  const slug = mapped.normalize('NFKD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '')
  return slug.slice(0, 80).replace(/-+$/, '')
}

const GUID = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** Ürün adresi: /urun/<okunur-ad>-<kimlik>. Ad boşsa yalnızca kimlik. */
export function productPath(id: string, name: string): string {
  const slug = slugify(name)
  return `/urun/${slug ? `${slug}-` : ''}${id}`
}

/** Adres parçasından ürün kimliği: "polo-yaka-tisort-<guid>" ya da yalnızca "<guid>"; kimlik yoksa null. */
export function productIdFromParam(param: string | undefined): string | null {
  const m = param ? GUID.exec(param) : null
  return m ? m[0].toLowerCase() : null
}

/** Açıklama için düz metin: boşluklar sadeleşir, uzunsa kelime sınırında kesilip "…" eklenir. */
export function metaDescription(text: string | null | undefined, fallback: string, max = 155): string {
  const plain = (text ?? '').replace(/\s+/g, ' ').trim()
  if (!plain) return fallback
  if (plain.length <= max) return plain
  const cut = plain.slice(0, max - 1)
  const space = cut.lastIndexOf(' ')
  return `${(space > max * 0.6 ? cut.slice(0, space) : cut).replace(/[.,;:\s]+$/, '')}…`
}

export interface ProductSchemaInput {
  name: string
  description: string | null
  images: string[]
  brand: string | null
  price: number
  currency: string
  inStock: boolean
  url: string
}

/** schema.org Product + Offer (arama motoru zengin sonucu için). */
export function productJsonLd(p: ProductSchemaInput): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'Product',
    name: p.name,
    ...(p.description ? { description: metaDescription(p.description, '', 500) } : {}),
    ...(p.images.length > 0 ? { image: p.images } : {}),
    ...(p.brand ? { brand: { '@type': 'Brand', name: p.brand } } : {}),
    offers: {
      '@type': 'Offer',
      url: p.url,
      price: p.price.toFixed(2),
      priceCurrency: p.currency,
      availability: p.inStock ? 'https://schema.org/InStock' : 'https://schema.org/OutOfStock',
    },
  }
}

export function productDescriptionFallback(shop: string, name: string, price: number, currency: string): string {
  return `${name} ${formatMoney(price, currency)} fiyatla ${shop} mağazasında.`
}

export interface PageMeta {
  title: string
  description: string
  /** Kanonik adres (tam URL); verilmezse etiket kaldırılır. */
  canonical?: string
  image?: string | null
  /** Arama sonuçları, süzgeçli ve sayfalı listeler dizinlenmesin. */
  noindex?: boolean
  type?: 'website' | 'product'
  jsonLd?: Record<string, unknown> | null
}

function setMeta(attr: 'name' | 'property', key: string, content: string | null) {
  let el = document.head.querySelector<HTMLMetaElement>(`meta[${attr}="${key}"]`)
  if (content === null) { el?.remove(); return }
  if (!el) { el = document.createElement('meta'); el.setAttribute(attr, key); document.head.appendChild(el) }
  el.setAttribute('content', content)
}

function setLink(rel: string, href: string | null) {
  let el = document.head.querySelector<HTMLLinkElement>(`link[rel="${rel}"]`)
  if (href === null) { el?.remove(); return }
  if (!el) { el = document.createElement('link'); el.setAttribute('rel', rel); document.head.appendChild(el) }
  el.setAttribute('href', href)
}

const JSONLD_ID = 'shop-jsonld'

/** Sayfa başlığı, açıklama, kanonik adres, Open Graph ve yapılandırılmış veriyi belgeye yazar; sayfadan çıkınca temizler. */
/** Geliştirme sunucusunda sekme başlığına eklenir; B2B ve B2C siteleri birbirine çok benzediği için hangisinde olunduğu hemen anlaşılsın. Üretim derlemesinde boştur. */
export const devTitleTag = import.meta.env.DEV ? '[B2C] ' : ''

export function usePageMeta(meta: PageMeta | null): void {
  const key = meta ? JSON.stringify(meta) : ''
  useEffect(() => {
    if (!meta) return
    document.title = devTitleTag + meta.title
    setMeta('name', 'description', meta.description)
    setMeta('name', 'robots', meta.noindex ? 'noindex, follow' : null)
    setMeta('property', 'og:title', meta.title)
    setMeta('property', 'og:description', meta.description)
    setMeta('property', 'og:type', meta.type ?? 'website')
    setMeta('property', 'og:image', meta.image ?? null)
    setLink('canonical', meta.canonical ?? null)
    document.getElementById(JSONLD_ID)?.remove()
    if (meta.jsonLd) {
      const script = document.createElement('script')
      script.id = JSONLD_ID
      script.type = 'application/ld+json'
      script.textContent = JSON.stringify(meta.jsonLd).replace(/</g, '\\u003c') // </script> kapanışına karşı
      document.head.appendChild(script)
    }
    return () => {
      document.getElementById(JSONLD_ID)?.remove()
      setMeta('name', 'robots', null)
      setMeta('property', 'og:image', null)
      setLink('canonical', null)
    }
    // meta nesnesi her render yeni olabilir; içeriği değişince çalışsın diye anahtar kullanılır.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])
}
