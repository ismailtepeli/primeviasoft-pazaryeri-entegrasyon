import { describe, expect, it } from 'vitest'
import { orderSteps, stoppedNote } from './orderSteps'

const states = (status: Parameters<typeof orderSteps>[0], channelStatus = '') => orderSteps(status, channelStatus).map((s) => s.state)

describe('orderSteps', () => {
  it('beş adımı sırayla verir', () => {
    expect(orderSteps('New', 'Created').map((s) => s.label)).toEqual(['Oluştu', 'Toplanıyor', 'Faturalandı', 'Etiket', 'Kargoda'])
  })

  it('yeni siparişte yalnızca ilk adım aktiftir', () => {
    expect(states('New', 'Created')).toEqual(['active', 'todo', 'todo', 'todo', 'todo'])
  })

  it('hazırlanırken Picking toplanıyor, Invoiced faturalandı adımını aktif yapar', () => {
    expect(states('Preparing', 'Picking')).toEqual(['done', 'active', 'todo', 'todo', 'todo'])
    expect(states('Preparing', 'Invoiced')).toEqual(['done', 'done', 'active', 'todo', 'todo'])
  })

  it('kargoda etiket dahil önceki adımlar tamamlanır', () => {
    expect(states('Shipped', 'Shipped')).toEqual(['done', 'done', 'done', 'done', 'active'])
  })

  it('teslim edilen siparişte tüm adımlar tamamdır', () => {
    expect(states('Delivered')).toEqual(['done', 'done', 'done', 'done', 'done'])
  })

  it('iptal ve iadede hiçbir adım aktif olmaz ve not gösterilir', () => {
    expect(states('Cancelled')).toEqual(['todo', 'todo', 'todo', 'todo', 'todo'])
    expect(stoppedNote('Returned')).toContain('iade')
    expect(stoppedNote('Cancelled')).toContain('iptal')
    expect(stoppedNote('New')).toBeNull()
  })
})
