import { describe, expect, it } from 'vitest'
import { failureReasons, listingBadge } from './listingStatus'

describe('listingStatus', () => {
  it('beş durumun da rozeti var', () => {
    expect(Object.values(listingBadge).map((b) => b.text)).toEqual(['Yayında', 'Eksik', 'Onayda', 'Pasif', 'Hata'])
  })

  it('hata nedenlerini satırlara böler, boşları ve tekrarları atar', () => {
    expect(failureReasons({ errorMessage: 'Barkod geçersiz\n\n Kategori eşlenmemiş \nBarkod geçersiz' }))
      .toEqual(['Barkod geçersiz', 'Kategori eşlenmemiş'])
    expect(failureReasons({ errorMessage: null })).toEqual([])
  })
})
