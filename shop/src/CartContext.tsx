import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { addLine, cartCount, parseStored, removeLine, setQuantity, type CartLine } from './cart'
import { useShop } from './ShopContext'

interface CartValue {
  lines: CartLine[]
  count: number
  add: (productId: string, quantity?: number) => void
  setQty: (productId: string, quantity: number) => void
  remove: (productId: string) => void
  clear: () => void
}

const Ctx = createContext<CartValue | null>(null)

export function useCart(): CartValue {
  const v = useContext(Ctx)
  if (!v) throw new Error('CartProvider gerekli')
  return v
}

const keyOf = (slug: string) => `shop-cart:${slug}`

function load(slug: string): CartLine[] {
  try { return parseStored(localStorage.getItem(keyOf(slug))) } catch { return [] }
}

/**
 * Sepet tarayıcıda (mağaza başına) tutulur; yalnızca ürün ve adet saklanır, fiyat ve stok her gösterimde sunucudan gelir.
 * Aynı tarayıcıdaki diğer sekmelerle senkron kalır.
 */
export function CartProvider({ children }: { children: ReactNode }) {
  const slug = useShop().tenant.slug
  const [lines, setLines] = useState<CartLine[]>(() => load(slug))

  const update = useCallback((next: (l: CartLine[]) => CartLine[]) => {
    setLines((current) => {
      const value = next(current)
      try { localStorage.setItem(keyOf(slug), JSON.stringify(value)) } catch { /* depolama kapalıysa sepet yalnızca bu oturumda kalır */ }
      return value
    })
  }, [slug])

  useEffect(() => {
    const onStorage = (e: StorageEvent) => { if (e.key === keyOf(slug)) setLines(load(slug)) }
    window.addEventListener('storage', onStorage)
    return () => window.removeEventListener('storage', onStorage)
  }, [slug])

  const value = useMemo<CartValue>(() => ({
    lines,
    count: cartCount(lines),
    add: (id, q = 1) => update((l) => addLine(l, id, q)),
    setQty: (id, q) => update((l) => setQuantity(l, id, q)),
    remove: (id) => update((l) => removeLine(l, id)),
    clear: () => update(() => []),
  }), [lines, update])

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
