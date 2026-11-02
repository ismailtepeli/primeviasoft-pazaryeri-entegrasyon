import { describe, expect, it } from 'vitest'
import { fulfillmentAction, initialTab, invoiceNumberError, nextStep, orderStatusBadge, orderTabs, ordersQuery } from './orderView'

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

  it('adresteki sekmeyi yalnızca geçerliyse kullanır', () => {
    expect(initialTab('preparing')).toBe('preparing')
    expect(initialTab('yok')).toBe('new')
    expect(initialTab(null)).toBe('new')
  })

  it('sorgu dizesini sekme, sayfa ve kırpılmış aramayla kurar', () => {
    expect(ordersQuery('new', '  ', 1)).toBe('tab=new&page=1&pageSize=20')
    expect(ordersQuery('shipped', ' TY-1 ', 3)).toBe('tab=shipped&page=3&pageSize=20&q=TY-1')
  })

  it('pazaryeri siparişinde sıradaki bildirim: Created → toplamaya al, Picking → faturalandı', () => {
    expect(fulfillmentAction('Trendyol', 'Created')).toEqual({ kind: 'picking', label: 'Toplamaya al' })
    expect(fulfillmentAction('Trendyol', ' picking ')?.kind).toBe('invoice')
  })

  it('faturalanmış, kargodaki ve B2B siparişlerde bildirim adımı yoktur', () => {
    for (const s of ['Invoiced', 'Shipped', 'Delivered', 'Cancelled', '']) expect(fulfillmentAction('Trendyol', s)).toBeNull()
    expect(fulfillmentAction('B2B', 'Created')).toBeNull()
  })

  it('fatura numarası boş ya da 64 karakterden uzunsa hata verir', () => {
    expect(invoiceNumberError('  ')).toBe('Fatura numarası zorunlu.')
    expect(invoiceNumberError('x'.repeat(65))).toContain('64')
    expect(invoiceNumberError(' FTR2026000000001 ')).toBeUndefined()
  })
})
