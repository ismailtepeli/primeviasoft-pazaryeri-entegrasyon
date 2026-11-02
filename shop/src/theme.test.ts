import { describe, expect, it } from 'vitest'
import { resolveTheme, themeOptions } from './theme'

describe('theme', () => {
  it('açık ve koyu seçimi sistem tercihini yok sayar', () => {
    expect(resolveTheme('light', true)).toBe('light')
    expect(resolveTheme('dark', false)).toBe('dark')
  })

  it('sistem seçimi sistem tercihini izler', () => {
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('system', false)).toBe('light')
  })

  it('üç seçenek sunulur ve açık tema ilk sıradadır', () => {
    expect(themeOptions.map((o) => o.value)).toEqual(['light', 'dark', 'system'])
  })
})
