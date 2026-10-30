import { useMemo } from 'react'
import { Folder, FolderOpen, FolderX, LayoutList } from 'lucide-react'
import type { Category } from '../api/types'
import './CategoryFilter.css'

interface Props {
  categories: Category[]
  /** '' = tümü, 'none' = kategorisiz, aksi halde kategori kimliği. */
  value: string
  onChange: (value: string) => void
}

/** Ürünler ekranının sol kategori ağacı: seçili dal açık gösterilir; sayılar alt kategorileri kapsar. */
export default function CategoryFilter({ categories, value, onChange }: Props) {
  const trail = useMemo(() => {
    const ids = new Set<string>()
    const byId = new Map(categories.map((c) => [c.id, c]))
    for (let cur = byId.get(value); cur && ids.size < 20; cur = cur.parentId ? byId.get(cur.parentId) : undefined) ids.add(cur.id)
    return ids
  }, [categories, value])

  const render = (parent: string | null) => {
    const items = categories.filter((c) => c.parentId === parent)
    if (items.length === 0) return null
    return (
      <ul className="cat-filter__list">
        {items.map((c) => {
          const open = trail.has(c.id)
          return (
            <li key={c.id}>
              <button type="button" className={`cat-filter__item${value === c.id ? ' cat-filter__item--active' : ''}${c.isActive ? '' : ' cat-filter__item--off'}`} aria-current={value === c.id ? 'true' : undefined} onClick={() => onChange(c.id)}>
                {open ? <FolderOpen size={16} aria-hidden /> : <Folder size={16} aria-hidden />}
                <span className="cat-filter__name">{c.name}</span>
                <span className="cat-filter__count">{c.totalProductCount}</span>
              </button>
              {open && <div className="cat-filter__nested">{render(c.id)}</div>}
            </li>
          )
        })}
      </ul>
    )
  }

  return (
    <nav className="cat-filter" aria-label="Kategori süzgeci">
      <button type="button" className={`cat-filter__item${value === '' ? ' cat-filter__item--active' : ''}`} aria-current={value === '' ? 'true' : undefined} onClick={() => onChange('')}>
        <LayoutList size={16} aria-hidden /><span className="cat-filter__name">Tüm ürünler</span>
      </button>
      {render(null)}
      <button type="button" className={`cat-filter__item${value === 'none' ? ' cat-filter__item--active' : ''}`} aria-current={value === 'none' ? 'true' : undefined} onClick={() => onChange('none')}>
        <FolderX size={16} aria-hidden /><span className="cat-filter__name">Kategorisiz</span>
      </button>
    </nav>
  )
}
