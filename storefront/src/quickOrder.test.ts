import { describe, expect, it } from 'vitest'
import { countLines, errorsAsText, summarize, validateUpload } from './quickOrder'

describe('quickOrder', () => {
  it('dosya biçimi ve boyutu denetlenir', () => {
    expect(validateUpload({ name: 'siparis.xlsx', size: 1000 })).toBeNull()
    expect(validateUpload({ name: 'LISTE.CSV', size: 1000 })).toBeNull()
    expect(validateUpload({ name: 'liste.txt', size: 1 })).toBeNull()
    expect(validateUpload({ name: 'resim.png', size: 1000 })).toMatch(/xlsx/)
    expect(validateUpload({ name: 'a.xlsx', size: 0 })).toMatch(/boş/)
    expect(validateUpload({ name: 'a.xlsx', size: 2_000_001 })).toMatch(/2 MB/)
  })

  it('yalnızca dolu satırlar sayılır', () => {
    expect(countLines('')).toBe(0)
    expect(countLines(' \n\r\n ')).toBe(0)
    expect(countLines('A;1\r\n\r\nB;2\n')).toBe(2)
  })

  it('özet eklenen ve sorunlu satırları söyler', () => {
    expect(summarize({ added: 2, errors: [] })).toBe('2 ürün sepete eklendi.')
    expect(summarize({ added: 0, errors: [{ row: 1, code: 'X', message: 'm' }] })).toBe('Sepete ürün eklenmedi. 1 satırda sorun var.')
    expect(summarize({ added: 1, errors: [{ row: 1, code: '', message: 'm' }, { row: 2, code: 'Y', message: 'n' }] })).toBe('1 ürün sepete eklendi. 2 satırda sorun var.')
  })

  it('hata metni satır numarası ve kodla biçimlenir', () => {
    expect(errorsAsText([{ row: 3, code: 'KALEM', message: 'Ürün bulunamadı.' }, { row: 5, code: '', message: 'Ürün kodu boş.' }]))
      .toBe('Satır 3 · KALEM: Ürün bulunamadı.\nSatır 5: Ürün kodu boş.')
  })
})
