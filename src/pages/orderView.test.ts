import { describe, expect, it } from 'vitest'
import { nextStep, orderStatusBadge, orderTabs, ordersQuery } from './orderView'

describe('orderView', () => {
  it('beş sekmeyi sayaçlarıyla sıralar', () => {
    const tabs = orderTabs({ new: 4, preparing: 3, shipped: 2, delivered: 1, cancelled: 0 })
    expect(tabs.map((t) => `${t.label}:${t.count}`)).toEqual(['Yeni:4', 'Hazırlanıyor:3', 'Kargoda:2', 'Teslim edildi:1', 'İptal / İade:0'])
    expect(tabs.map((t) => t.value)).toEqual(['new', 'preparing', 'shipped', 'delivered', 'cancelled'])
  })

  it('her durum için etiket ve sıradaki adım tanımlıdır', () => {
    expect(orderStatusBadge.Delivered).toEqual({ text: 'Teslim edildi', variant: 'success' })
    expect(nextStep('New')).toBe('Toplamaya al')
    expect(nextStep('Preparing')).toBe('Faturala ve etiket yazdır')
    expect(nextStep('Shipped')).toBe('Teslimat bekleniyor')
    expect((['Delivered', 'Cancelled', 'Returned'] as const).map(nextStep)).toEqual(['—', '—', '—'])
  })

  it('sorgu dizesini sekme, sayfa ve kırpılmış aramayla kurar', () => {
    expect(ordersQuery('new', '  ', 1)).toBe('tab=new&page=1&pageSize=20')
    expect(ordersQuery('shipped', ' TY-1 ', 3)).toBe('tab=shipped&page=3&pageSize=20&q=TY-1')
  })
})
