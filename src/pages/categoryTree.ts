import type { Category } from '../api/types'

export interface TreeRow { category: Category; depth: number; hasChildren: boolean }

/** Sunucu ağaç sırasıyla gelen düz listeden, açık dallara göre görünen satırları üretir. */
export function visibleRows(all: Category[], collapsed: ReadonlySet<string>): TreeRow[] {
  const byId = new Map(all.map((c) => [c.id, c]))
  const hasChildren = new Set(all.map((c) => c.parentId).filter((p): p is string => !!p))
  const depthOf = (c: Category): number => { let d = 0; for (let p = c.parentId; p && d < 10; p = byId.get(p)?.parentId ?? null) d++; return d }
  const rows: TreeRow[] = []
  for (const c of all) {
    let hidden = false
    for (let p = c.parentId; p; p = byId.get(p)?.parentId ?? null) if (collapsed.has(p)) { hidden = true; break }
    if (!hidden) rows.push({ category: c, depth: depthOf(c), hasChildren: hasChildren.has(c.id) })
  }
  return rows
}

export function siblingsOf(all: Category[], c: Category): Category[] {
  return all.filter((x) => x.parentId === c.parentId)
}

/** Kardeşler arasında bir adım yukarı/aşağı taşınmış sıra (kimlik listesi); sınırdaysa null. */
export function movedOrder(all: Category[], c: Category, direction: -1 | 1): string[] | null {
  const ids = siblingsOf(all, c).map((x) => x.id)
  const i = ids.indexOf(c.id)
  const j = i + direction
  if (i < 0 || j < 0 || j >= ids.length) return null
  ;[ids[i], ids[j]] = [ids[j], ids[i]]
  return ids
}

/** Kategoriyi taşıyabileceği üst kategoriler: kendisi ve alt dalı hariç, derinlik sınırı aşılmayanlar. */
export function moveTargets(all: Category[], c: Category, maxDepth = 5): Category[] {
  const children = new Map<string | null, Category[]>()
  for (const x of all) children.set(x.parentId, [...(children.get(x.parentId) ?? []), x])
  const subtree = new Set<string>()
  const walk = (id: string) => { subtree.add(id); for (const k of children.get(id) ?? []) walk(k.id) }
  walk(c.id)
  const height = (id: string): number => 1 + Math.max(0, ...(children.get(id) ?? []).map((k) => height(k.id)))
  const byId = new Map(all.map((x) => [x.id, x]))
  const depthOf = (x: Category): number => { let d = 1; for (let p = x.parentId; p; p = byId.get(p)?.parentId ?? null) d++; return d }
  const h = height(c.id)
  return all.filter((t) => !subtree.has(t.id) && t.id !== c.parentId && depthOf(t) + h <= maxDepth)
}

/** Kategori adresinin kısa gösterimi: "/giyim/tisort". */
export const slugLabel = (c: Category): string => `/${c.slugPath}`
