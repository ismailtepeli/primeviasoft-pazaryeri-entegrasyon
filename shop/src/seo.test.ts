import { describe, expect, it } from 'vitest'
import { metaDescription, productIdFromParam, productJsonLd, productPath, slugify } from './seo'

const id = '7763b13f-d895-48c9-b3c8-b8b0092de605'

describe('seo', () => {
  it('Türkçe karakterleri sadeleştirip okunur adres üretir', () => {
    expect(slugify('Polo Yaka Tişört')).toBe('polo-yaka-tisort')
    expect(slugify('ÇAĞRI Işık Öğüt Şef Ünlü')).toBe('cagri-isik-ogut-sef-unlu')
    expect(slugify("Tükenmez Kalem Mavi (12'li)")).toBe('tukenmez-kalem-mavi-12-li')
    expect(slugify('  ---  ')).toBe('')
    expect(slugify('a'.repeat(200)).length).toBe(80)
    expect(slugify('İstanbul')).toBe('istanbul')
  })

  it('ürün adresini kurar ve kimliği geri okur', () => {
    expect(productPath(id, 'Polo Yaka Tişört')).toBe(`/urun/polo-yaka-tisort-${id}`)
    expect(productPath(id, '***')).toBe(`/urun/${id}`)
    expect(productIdFromParam(`polo-yaka-tisort-${id}`)).toBe(id)
    expect(productIdFromParam(id.toUpperCase())).toBe(id)
    expect(productIdFromParam('polo-yaka')).toBeNull()
    expect(productIdFromParam(undefined)).toBeNull()
  })

  it('açıklamayı düz metne çevirir, uzunsa kelime sınırında keser', () => {
    expect(metaDescription('  Kısa\n\n açıklama  ', 'yedek')).toBe('Kısa açıklama')
    expect(metaDescription(null, 'yedek')).toBe('yedek')
    const long = 'kelime '.repeat(60)
    const cut = metaDescription(long, 'yedek', 50)
    expect(cut.length).toBeLessThanOrEqual(50)
    expect(cut.endsWith('…')).toBe(true)
    expect(cut).not.toMatch(/kelim…$/) // kelimenin ortasından kesilmez
  })

  it('schema.org ürün verisi fiyat, stok ve markayı içerir', () => {
    const ld = productJsonLd({ name: 'Polo', description: 'Güzel', images: ['/a.jpg'], brand: 'Demo', price: 179.9, currency: 'TRY', inStock: true, url: 'https://x/urun/p' }) as Record<string, unknown>
    expect(ld['@type']).toBe('Product')
    expect(ld.brand).toEqual({ '@type': 'Brand', name: 'Demo' })
    expect(ld.offers).toMatchObject({ price: '179.90', priceCurrency: 'TRY', availability: 'https://schema.org/InStock' })
    const out = productJsonLd({ name: 'X', description: null, images: [], brand: null, price: 1, currency: 'TRY', inStock: false, url: 'u' }) as Record<string, unknown>
    expect(out).not.toHaveProperty('image')
    expect(out).not.toHaveProperty('brand')
    expect((out.offers as Record<string, unknown>).availability).toBe('https://schema.org/OutOfStock')
  })
})
