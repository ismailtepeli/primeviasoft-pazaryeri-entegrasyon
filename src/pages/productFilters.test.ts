import { describe, expect, it } from 'vitest'
import { productsQuery } from './productFilters'

describe('productsQuery', () => {
  it('varsayılan istek yalnızca sayfalamayı taşır', () => {
    expect(productsQuery('', 'all', 1)).toBe('page=1&pageSize=20')
  })

  it('aramayı kırpar ve türkçe karakteri kodlar', () => {
    expect(productsQuery('  çanta ', 'all', 2)).toBe('page=2&pageSize=20&q=%C3%A7anta')
  })

  it('filtreyi ekler', () => {
    expect(productsQuery('', 'error', 1)).toContain('filter=error')
    expect(productsQuery('', 'pending', 1)).toContain('filter=pending')
  })
})
