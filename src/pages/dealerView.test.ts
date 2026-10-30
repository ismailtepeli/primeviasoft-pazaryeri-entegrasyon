import { describe, expect, it } from 'vitest'
import type { DealerDetail } from '../api/types'
import { dealerActions, dealersQuery, dealerStatusBadge, dealerTabs, emptyDealerForm, formFromDealer, toDealerInput, validateDealerForm, validateUserForm } from './dealerView'

const valid = { ...emptyDealerForm, name: 'Mavi Ticaret', taxNumber: '1234567890', email: 'info@mavi.com' }

describe('dealerView', () => {
  it('sekmeleri sayaçlarıyla sıralar: önce başvurular', () => {
    expect(dealerTabs({ pending: 2, active: 5, passive: 1 }).map((t) => `${t.label}:${t.count}`)).toEqual(['Başvurular:2', 'Aktif:5', 'Pasif:1'])
  })

  it('durum rozetleri ve geçişler sunucu kurallarıyla aynıdır', () => {
    expect(dealerStatusBadge.Pending.text).toBe('Onay bekliyor')
    expect(dealerActions('Pending').map((a) => a.action)).toEqual(['approve', 'deactivate'])
    expect(dealerActions('Active').map((a) => a.action)).toEqual(['deactivate'])
    expect(dealerActions('Passive').map((a) => a.action)).toEqual(['activate'])
  })

  it('sorgu dizesini durum, sayfa ve kırpılmış aramayla kurar', () => {
    expect(dealersQuery('pending', ' ', 1)).toBe('status=pending&page=1&pageSize=20')
    expect(dealersQuery('active', ' mavi ', 2)).toBe('status=active&page=2&pageSize=20&q=mavi')
  })

  it('firma formunu doğrular: ad, vergi numarası 10/11 hane, e-posta', () => {
    expect(validateDealerForm(valid)).toEqual({})
    expect(validateDealerForm({ ...valid, taxNumber: '12345678901' })).toEqual({})
    expect(Object.keys(validateDealerForm(emptyDealerForm)).sort()).toEqual(['email', 'name', 'taxNumber'])
    expect(validateDealerForm({ ...valid, taxNumber: '123456789' }).taxNumber).toContain('10 (VKN)')
    expect(validateDealerForm({ ...valid, taxNumber: '12345678ab' }).taxNumber).toBeDefined()
    expect(validateDealerForm({ ...valid, email: 'gecersiz' }).email).toBeDefined()
    expect(validateDealerForm({ ...valid, name: 'x'.repeat(201) }).name).toContain('200')
  })

  it('formu API girdisine çevirir: boşlar null, grup boşsa null', () => {
    expect(toDealerInput({ ...valid, taxOffice: ' Kadıköy ', city: ' ' })).toEqual({
      name: 'Mavi Ticaret', taxNumber: '1234567890', taxOffice: 'Kadıköy', contactName: null, email: 'info@mavi.com',
      phone: null, address: null, city: null, notes: null, groupId: null,
    })
    expect(toDealerInput({ ...valid, groupId: 'g1' }).groupId).toBe('g1')
  })

  it('kayıtlı bayiden form üretir (null alanlar boş metin)', () => {
    const d = { id: '1', name: 'A', taxNumber: '1234567890', taxOffice: null, contactName: 'Ayşe', email: 'a@a.com', phone: null, address: null, city: 'Ankara', notes: null, status: 'Active', groupId: 'g', createdAt: '', approvedAt: null, users: [] } as DealerDetail
    expect(formFromDealer(d)).toMatchObject({ taxOffice: '', contactName: 'Ayşe', city: 'Ankara', groupId: 'g' })
  })

  it('kullanıcı formunu doğrular', () => {
    expect(validateUserForm({ email: 'a@b.com', fullName: 'Ad', password: '12345678' })).toEqual({})
    expect(Object.keys(validateUserForm({ email: 'x', fullName: ' ', password: 'kisa' })).sort()).toEqual(['email', 'fullName', 'password'])
  })
})
