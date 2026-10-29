import { describe, expect, it } from 'vitest'
import { secretReminderText } from './amazonOAuth'

describe('secretReminderText', () => {
  it('hatırlatma gerekmiyorsa metin yok', () => {
    expect(secretReminderText(100, false)).toBeNull()
    expect(secretReminderText(null, true)).toBeNull()
  })

  it('kalan günü söyler', () => {
    expect(secretReminderText(12, true)).toContain('12 gün sonra')
  })

  it('bugün dolan secret için bugün der', () => {
    expect(secretReminderText(0, true)).toContain('bugün')
  })

  it('süresi dolmuşsa kaç gün önce dolduğunu ve yetkilendirmenin çalışmayacağını söyler', () => {
    const text = secretReminderText(-5, true)
    expect(text).toContain('5 gün önce doldu')
    expect(text).toContain('çalışmaz')
  })
})
