import { describe, expect, it } from 'vitest'
import { routeFor } from './routes'

describe('routeFor', () => {
  it('ürün sayfalarını bulur', () => {
    expect(routeFor('/urunler/yeni').title).toBe('Yeni ürün')
    expect(routeFor('/urunler/3f2a-bc').title).toBe('Ürünü düzenle')
    expect(routeFor('/urunler').title).toBe('Ürünler')
    expect(routeFor('/senkron-hatalari').title).toBe('Senkron hataları')
    expect(routeFor('/bilinmeyen').title).toBe('Genel bakış')
  })
})
