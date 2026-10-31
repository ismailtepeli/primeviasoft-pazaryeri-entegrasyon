import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { tokenStore } from './client'

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

describe('tokenStore (Beni hatırla)', () => {
  let local: Storage
  let session: Storage
  beforeEach(() => {
    local = fakeStorage(); session = fakeStorage()
    vi.stubGlobal('localStorage', local)
    vi.stubGlobal('sessionStorage', session)
  })
  afterEach(() => vi.unstubAllGlobals())

  it('hatırla işaretliyse kalıcı depoya yazar', () => {
    tokenStore.set('t1', true)
    expect(local.getItem('pz.token')).toBe('t1')
    expect(session.getItem('pz.token')).toBeNull()
    expect(tokenStore.get()).toBe('t1')
  })

  it('işaretli değilse yalnızca oturum deposuna yazar', () => {
    tokenStore.set('t2', false)
    expect(session.getItem('pz.token')).toBe('t2')
    expect(local.getItem('pz.token')).toBeNull()
    expect(tokenStore.get()).toBe('t2')
  })

  it('tercih değişince eski kayıt temizlenir, aynı anda tek kopya kalır', () => {
    tokenStore.set('eski', true)
    tokenStore.set('yeni', false)
    expect(local.getItem('pz.token')).toBeNull()
    expect(tokenStore.get()).toBe('yeni')
  })

  it('önceden kalıcı kaydedilmiş oturum okunmaya devam eder, çıkış ikisini de siler', () => {
    local.setItem('pz.token', 'eskiOturum')
    expect(tokenStore.get()).toBe('eskiOturum')
    session.setItem('pz.token', 'x')
    tokenStore.clear()
    expect(tokenStore.get()).toBeNull()
    expect(local.length + session.length).toBe(0)
  })

  it('depolama erişimi hata verirse çökmez', () => {
    const broken = { getItem: () => { throw new Error('x') }, setItem: () => { throw new Error('x') }, removeItem: () => { throw new Error('x') } }
    vi.stubGlobal('localStorage', broken)
    vi.stubGlobal('sessionStorage', broken)
    expect(tokenStore.get()).toBeNull()
    expect(() => tokenStore.set('t', true)).not.toThrow()
    expect(() => tokenStore.clear()).not.toThrow()
  })
})
