import { describe, expect, it } from 'vitest'
import { resolveTenant } from './tenant'

describe('resolveTenant', () => {
  it('alt alan adından kiracıyı çözer', () => {
    expect(resolveTenant('demo.b2b.site', '/', 'b2b.site')).toEqual({ slug: 'demo', basePath: '' })
    expect(resolveTenant('DEMO.B2B.site', '/urunler', '.b2b.site')).toEqual({ slug: 'demo', basePath: '' })
  })

  it('www, çok seviyeli ve geçersiz etiketleri reddeder', () => {
    expect(resolveTenant('www.b2b.site', '/', 'b2b.site')).toBeNull()
    expect(resolveTenant('a.b.b2b.site', '/', 'b2b.site')).toBeNull()
    expect(resolveTenant('b2b.site', '/', 'b2b.site')).toBeNull()
    expect(resolveTenant('-x.b2b.site', '/', 'b2b.site')).toBeNull()
  })

  it('alan adı eşleşmezse yol önekine düşer', () => {
    expect(resolveTenant('localhost', '/t/demo', 'b2b.site')).toEqual({ slug: 'demo', basePath: '/t/demo' })
    expect(resolveTenant('localhost', '/t/Demo/giris')).toEqual({ slug: 'demo', basePath: '/t/demo' })
    expect(resolveTenant('baska.com', '/t/demo/sepet', 'b2b.site')?.slug).toBe('demo')
  })

  it('kiracı belirtilmemişse null döner', () => {
    expect(resolveTenant('localhost', '/')).toBeNull()
    expect(resolveTenant('localhost', '/t/')).toBeNull()
    expect(resolveTenant('localhost', '/tx/demo')).toBeNull()
    expect(resolveTenant('localhost', '/t/de_mo')).toBeNull()
  })

  it('alt alan adı yol önekinden önceliklidir', () => {
    expect(resolveTenant('acme.b2b.site', '/t/demo', 'b2b.site')).toEqual({ slug: 'acme', basePath: '' })
  })
})
