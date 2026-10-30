import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { StoreApiError, storeApi } from './api'
import { emptyCart, type Cart } from './orders'
import { useStore } from './StoreContext'

interface CartValue {
  cart: Cart
  /** Sepete adet ekler; sunucu reddederse hata mesajını fırlatır. */
  add: (productId: string, quantity: number) => Promise<void>
  setQuantity: (productId: string, quantity: number) => Promise<void>
  refresh: () => Promise<void>
  replace: (cart: Cart) => void
}

const Ctx = createContext<CartValue | null>(null)

export function useCart(): CartValue {
  const v = useContext(Ctx)
  if (!v) throw new Error('CartProvider gerekli')
  return v
}

/** Bayinin sunucudaki sepeti; giriş yapılmışsa yüklenir, çıkışta sıfırlanır. */
export function CartProvider({ children }: { children: ReactNode }) {
  const { token, logout } = useStore()
  const [cart, setCart] = useState<Cart>(emptyCart)

  const refresh = useCallback(async () => {
    if (!token) { setCart(emptyCart); return }
    try { setCart(await storeApi<Cart>('/cart', {}, token)) }
    catch (e) { if (e instanceof StoreApiError && e.status === 401) logout() }
  }, [token, logout])

  useEffect(() => { void refresh() }, [refresh])

  const add = useCallback(async (productId: string, quantity: number) => {
    setCart(await storeApi<Cart>('/cart/items', { method: 'POST', body: JSON.stringify({ productId, quantity }) }, token))
  }, [token])

  const setQuantity = useCallback(async (productId: string, quantity: number) => {
    setCart(await storeApi<Cart>(`/cart/items/${productId}`, { method: 'PUT', body: JSON.stringify({ quantity }) }, token))
  }, [token])

  const value = useMemo(() => ({ cart, add, setQuantity, refresh, replace: setCart }), [cart, add, setQuantity, refresh])
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
