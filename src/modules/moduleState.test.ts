import { describe, expect, it } from 'vitest'
import type { ModuleStatus } from '../api/types'
import { routes, routeFor } from '../routes'
import { anyModuleEnabled, enabledSet, isModuleEnabled, isRouteEnabled, toggleBlockReason, visibleRoutes } from './moduleState'

const m = (id: string, enabled: boolean, dependsOn: string[] = [], core = false): ModuleStatus =>
  ({ id, name: id.toUpperCase(), description: '', core, enabled, dependsOn })

const list = [m('core', true, [], true), m('marketplace', true, ['core']), m('stock-price', true, ['marketplace']), m('orders', false, ['core']), m('invoices', false, ['orders'])]

describe('moduleState', () => {
  it('liste gelmediyse her rota görünür, geldiyse yalnızca açık modüllerinkiler', () => {
    expect(visibleRoutes(routes, enabledSet(undefined))).toHaveLength(routes.length)
    const paths = visibleRoutes(routes, enabledSet(list)).map((r) => r.path)
    expect(paths).toContain('/urunler')
    expect(paths).toContain('/magazalar')
    expect(paths).toContain('/stok-fiyat')
    expect(paths).not.toContain('/siparisler')
    expect(paths).not.toContain('/iadeler')
    expect(paths).not.toContain('/faturalar')
  })

  it('çekirdek rotalar (modülsüz) her zaman görünür, Ayarlar dahil', () => {
    expect(isRouteEnabled({ module: undefined }, new Set())).toBe(true)
    expect(visibleRoutes(routes, new Set()).map((r) => r.path)).toContain('/ayarlar')
  })

  it('modül açıklığı: yüklenmediyse açık sayılır, kümede yoksa kapalı', () => {
    expect(isModuleEnabled('orders', null)).toBe(true)
    expect(isModuleEnabled('orders', new Set(['core']))).toBe(false)
    expect(anyModuleEnabled(['marketplace', 'orders'], new Set(['orders']))).toBe(true)
    expect(anyModuleEnabled(['marketplace', 'orders'], new Set(['core']))).toBe(false)
  })

  it('rota adresinden modül bulunur', () => {
    expect(routeFor('/siparisler').module).toBe('orders')
    expect(routeFor('/esleme').module).toBe('marketplace')
    expect(routeFor('/urunler').module).toBeUndefined()
  })

  it('anahtarın engel nedeni: çekirdek, bağımlısı açık, bağımlılığı kapalı, serbest', () => {
    expect(toggleBlockReason(list[0], list)).toBe('Her zaman açık')
    expect(toggleBlockReason(list[1], list)).toBe('Önce kapatın: STOCK-PRICE')
    expect(toggleBlockReason(list[4], list)).toBe('Önce açın: ORDERS')
    expect(toggleBlockReason(list[3], list)).toBeNull() // orders: bağımlılığı (core) açık
    expect(toggleBlockReason(list[2], list)).toBeNull() // stock-price: bağımlısı yok
  })
})
