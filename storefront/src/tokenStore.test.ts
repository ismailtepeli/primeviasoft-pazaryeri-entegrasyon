import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { tokenStore as store } from './api'

function fakeStorage(): Storage {
  const m = new Map<string, string>()
  return {
    getItem: (k) => m.get(k) ?? null,
    setItem: (k, v) => { m.set(k, v) },
    removeItem: (k) => { m.delete(k) },
    clear: () => m.clear(),
    key: (i) => [...m.keys()][i] ?? null,
    get length() { return m.size },
  }
}

describe('bayi tokenStore (Beni hatırla)', () => {
  let local: Storage
  let session: Storage
  beforeEach(() => {
    local = fakeStorage(); session = fakeStorage()
    vi.stubGlobal('localStorage', local)
    vi.stubGlobal('sessionStorage', session)
  })
  afterEach(() => vi.unstubAllGlobals())

  it('hatırla işaretliyse kalıcı depoya yazar', () => {
    store.set('demo', 't1', true)
    expect(local.getItem('store.token.demo')).toBe('t1')
    expect(session.getItem('store.token.demo')).toBeNull()
    expect(store.get('demo')).toBe('t1')
  })

  it('işaretli değilse yalnızca oturum deposuna yazar', () => {
    store.set('demo', 't2', false)
    expect(session.getItem('store.token.demo')).toBe('t2')
    expect(local.getItem('store.token.demo')).toBeNull()
    expect(store.get('demo')).toBe('t2')
  })

  it('tercih değişince eski kayıt temizlenir, aynı anda tek kopya kalır', () => {
    store.set('demo', 'eski', true)
    store.set('demo', 'yeni', false)
    expect(local.getItem('store.token.demo')).toBeNull()
    expect(store.get('demo')).toBe('yeni')
  })

  it('önceden kalıcı kaydedilmiş oturum okunmaya devam eder, çıkış ikisini de siler', () => {
    local.setItem('store.token.demo', 'eskiOturum')
    expect(store.get('demo')).toBe('eskiOturum')
    session.setItem('store.token.demo', 'x')
    store.clear('demo')
    expect(store.get('demo')).toBeNull()
    expect(local.length + session.length).toBe(0)
  })

  it('depolama erişimi hata verirse çökmez', () => {
    const broken = { getItem: () => { throw new Error('x') }, setItem: () => { throw new Error('x') }, removeItem: () => { throw new Error('x') } }
    vi.stubGlobal('localStorage', broken)
    vi.stubGlobal('sessionStorage', broken)
    expect(store.get('demo')).toBeNull()
    expect(() => store.set('demo', 't', true)).not.toThrow()
    expect(() => store.clear('demo')).not.toThrow()
  })
})
