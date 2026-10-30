import { describe, expect, it } from 'vitest'
import { b2bNextStep, b2bStatusBadge, balanceLabel, primaryAction, validatePaymentForm, validateReason, validateShipment } from './b2bOrderView'

describe('b2bOrderView', () => {
  it('B2B durum adları onay akışını anlatır', () => {
    expect(b2bStatusBadge.New.text).toBe('Onay bekliyor')
    expect(b2bStatusBadge.Preparing.text).toContain('Onaylandı')
    expect(b2bStatusBadge.Cancelled.text).toContain('ret')
  })

  it('sıradaki adım B2B akışını izler', () => {
    expect(b2bNextStep('New')).toBe('Onayla ya da reddet')
    expect(b2bNextStep('Preparing')).toBe('Sevk et')
    expect(b2bNextStep('Delivered')).toBe('—')
  })

  it('birincil eylem ilerletici adımdır', () => {
    expect(primaryAction(['Approve', 'Reject', 'Cancel'])).toBe('Approve')
    expect(primaryAction(['Ship', 'Cancel'])).toBe('Ship')
    expect(primaryAction(['Deliver'])).toBe('Deliver')
    expect(primaryAction([])).toBeNull()
    expect(primaryAction(['Cancel'])).toBeNull()
  })

  it('sevk bilgisi doğrulaması', () => {
    expect(validateShipment('Yurtiçi', 'TR1')).toBeNull()
    expect(validateShipment(' ', 'TR1')).toMatch(/Kargo firması/)
    expect(validateShipment('Yurtiçi', '')).toMatch(/Takip/)
    expect(validateShipment('x'.repeat(101), '1')).not.toBeNull()
  })

  it('gerekçe doğrulaması zorunluluğa göre', () => {
    expect(validateReason('', true)).not.toBeNull()
    expect(validateReason('', false)).toBeNull()
    expect(validateReason('x'.repeat(501), false)).not.toBeNull()
  })

  it('bakiye etiketi ve ödeme tutarı', () => {
    expect(balanceLabel(10)).toBe('Bayi borçlu')
    expect(balanceLabel(-1)).toBe('Bayi alacaklı')
    expect(balanceLabel(0)).toBe('Bakiye sıfır')
    expect(validatePaymentForm('12,5')).toBeNull()
    expect(validatePaymentForm('0')).not.toBeNull()
    expect(validatePaymentForm('abc')).not.toBeNull()
    expect(validatePaymentForm('')).not.toBeNull()
  })
})
