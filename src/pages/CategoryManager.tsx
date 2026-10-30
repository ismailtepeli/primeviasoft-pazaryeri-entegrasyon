import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowDown, ArrowUp, ChevronDown, ChevronRight, EyeOff, Folder, FolderOpen, Pencil } from 'lucide-react'
import { api } from '../api/client'
import type { Category } from '../api/types'
import { Button, Select, TextInput } from '../components'
import { movedOrder, moveTargets, slugLabel, visibleRows } from './categoryTree'
import './CategoryManager.css'

type Message = { ok: boolean; text: string }

interface EditorProps {
  category: Category
  all: Category[]
  onClose: () => void
  onChanged: (text: string) => void
  onError: (text: string) => void
}

/** Seçili kategorinin düzenleme paneli: ad, aktif/pasif, görsel, üst kategori ve silme. */
function CategoryEditor({ category, all, onClose, onChanged, onError }: EditorProps) {
  const [name, setName] = useState(category.name)
  const [active, setActive] = useState(category.isActive)
  const [image, setImage] = useState(category.imageUrl ?? '')
  const [parent, setParent] = useState(category.parentId ?? '')
  const [moveTo, setMoveTo] = useState('')
  const targets = useMemo(() => moveTargets(all, category), [all, category])
  const deleteTargets = useMemo(() => all.filter((c) => c.id !== category.id), [all, category])

  async function save() {
    try {
      if (name.trim() !== category.name) await api(`/categories/${category.id}`, { method: 'PUT', body: JSON.stringify({ name }) })
      if (active !== category.isActive || (image.trim() || null) !== category.imageUrl)
        await api(`/categories/${category.id}/settings`, { method: 'PUT', body: JSON.stringify({ isActive: active, imageUrl: image.trim() }) })
      if (parent !== (category.parentId ?? '')) await api(`/categories/${category.id}/move`, { method: 'PUT', body: JSON.stringify({ parentId: parent || null }) })
      onChanged('Kategori kaydedildi.')
    } catch (e) { onError((e as Error).message) }
  }

  async function remove() {
    try {
      await api(`/categories/${category.id}${moveTo ? `?moveProductsTo=${moveTo}` : ''}`, { method: 'DELETE' })
      onChanged(`"${category.path}" silindi.`)
    } catch (e) { onError((e as Error).message) }
  }

  return (
    <section className="category-editor" aria-label={`${category.name} kategorisini düzenle`}>
      <div className="category-editor__grid">
        <TextInput label="Kategori adı" value={name} onChange={(e) => setName(e.target.value)} />
        <Select label="Üst kategori" value={parent} onChange={(e) => setParent(e.target.value)}>
          <option value="">Ana kategori (kök)</option>
          {category.parentId && !targets.some((t) => t.id === category.parentId) && <option value={category.parentId}>{all.find((c) => c.id === category.parentId)?.path} (mevcut)</option>}
          {targets.map((t) => <option key={t.id} value={t.id}>{t.path}</option>)}
        </Select>
        <TextInput label="Görsel adresi" hint="http ya da https; boş bırakılırsa görsel kaldırılır" value={image} onChange={(e) => setImage(e.target.value)} />
      </div>
      <label className="category-check"><input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} /> Aktif (pasif kategori bayi sitesinde görünmez)</label>
      <p className="category-meta">Adres kodu: <code>{slugLabel(category)}</code> · {category.productCount} ürün doğrudan, {category.totalProductCount} ürün alt kategorilerle</p>
      <div className="category-editor__actions">
        <Button onClick={save} disabled={!name.trim()}>Kaydet</Button>
        <Button onClick={onClose}>Vazgeç</Button>
      </div>
      <div className="category-danger">
        <Select label={category.productCount > 0 ? 'Silmeden önce ürünleri taşı' : 'Ürünleri taşı (isteğe bağlı)'} value={moveTo} onChange={(e) => setMoveTo(e.target.value)}>
          <option value="">Taşıma hedefi seçin</option>
          {deleteTargets.map((t) => <option key={t.id} value={t.id}>{t.path}</option>)}
        </Select>
        <Button onClick={remove}>Kategoriyi sil</Button>
      </div>
    </section>
  )
}

/** Kategori ağacı: aç/kapa, sıralama (yukarı/aşağı), düzenleme ve silme. Yeni kategori ürün sayfasından, Excel'den ya da buradan eklenir. */
export default function CategoryManager({ onClose }: { onClose: () => void }) {
  const [categories, setCategories] = useState<Category[]>([])
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set())
  const [editing, setEditing] = useState<string>()
  const [message, setMessage] = useState<Message>()
  const [newName, setNewName] = useState('')
  const [newParent, setNewParent] = useState('')

  const load = useCallback(() => api<Category[]>('/categories').then(setCategories).catch((e: Error) => setMessage({ ok: false, text: e.message })), [])
  useEffect(() => { void load() }, [load])

  const rows = useMemo(() => visibleRows(categories, collapsed), [categories, collapsed])
  const editingCategory = categories.find((c) => c.id === editing)

  function toggle(id: string) {
    setCollapsed((s) => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n })
  }

  async function reorder(c: Category, direction: -1 | 1) {
    const ids = movedOrder(categories, c, direction)
    if (!ids) return
    try { setCategories(await api<Category[]>('/categories/order', { method: 'PUT', body: JSON.stringify({ parentId: c.parentId, ids }) })); setMessage(undefined) }
    catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
  }

  async function add() {
    try {
      await api('/categories', { method: 'POST', body: JSON.stringify({ name: newName, parentId: newParent || null }) })
      setNewName(''); setMessage({ ok: true, text: 'Kategori eklendi.' }); await load()
    } catch (e) { setMessage({ ok: false, text: (e as Error).message }) }
  }

  return (
    <section className="ps-card category-manager" aria-labelledby="cat-title">
      <header className="category-manager__head">
        <h2 id="cat-title">Kategoriler</h2>
        <Button onClick={onClose}>Kapat</Button>
      </header>
      {message && <p className={message.ok ? 'category-ok' : 'category-error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}

      <div className="category-add">
        <TextInput label="Yeni kategori" value={newName} onChange={(e) => setNewName(e.target.value)} />
        <Select label="Üst kategori" value={newParent} onChange={(e) => setNewParent(e.target.value)}>
          <option value="">Ana kategori (kök)</option>
          {categories.filter((c) => c.path.split(' > ').length < 5).map((c) => <option key={c.id} value={c.id}>{c.path}</option>)}
        </Select>
        <Button onClick={add} disabled={!newName.trim()}>Ekle</Button>
      </div>

      {categories.length === 0 ? <p className="category-meta">Henüz kategori yok. Yukarıdan ekleyebilir, ürün sayfasından ya da Excel yüklemesiyle oluşturabilirsiniz.</p> : (
        <ul className="category-tree" role="tree" aria-label="Kategori ağacı">
          {rows.map(({ category: c, depth, hasChildren }) => {
            const siblingsCount = categories.filter((x) => x.parentId === c.parentId).length
            const index = categories.filter((x) => x.parentId === c.parentId).findIndex((x) => x.id === c.id)
            const open = !collapsed.has(c.id)
            return (
              <li key={c.id} role="treeitem" aria-expanded={hasChildren ? open : undefined} aria-level={depth + 1} className={`category-row${c.isActive ? '' : ' category-row--off'}${editing === c.id ? ' category-row--editing' : ''}`} style={{ paddingLeft: `${depth * 24 + 8}px` }}>
                <button type="button" className="icon-btn" onClick={() => hasChildren && toggle(c.id)} aria-label={hasChildren ? `${c.name} dalını ${open ? 'kapat' : 'aç'}` : undefined} tabIndex={hasChildren ? 0 : -1} disabled={!hasChildren}>
                  {hasChildren ? (open ? <ChevronDown size={16} aria-hidden /> : <ChevronRight size={16} aria-hidden />) : <span className="icon-btn__spacer" />}
                </button>
                {hasChildren && open ? <FolderOpen size={18} aria-hidden className="category-row__icon" /> : <Folder size={18} aria-hidden className="category-row__icon" />}
                <span className="category-row__name">{c.name}</span>
                {!c.isActive && <span className="category-badge"><EyeOff size={12} aria-hidden /> Pasif</span>}
                <span className="category-row__count">{c.productCount} ürün{c.totalProductCount !== c.productCount ? ` · toplam ${c.totalProductCount}` : ''}</span>
                <span className="category-row__actions">
                  <button type="button" className="icon-btn" aria-label={`${c.name} yukarı taşı`} disabled={index === 0} onClick={() => reorder(c, -1)}><ArrowUp size={16} aria-hidden /></button>
                  <button type="button" className="icon-btn" aria-label={`${c.name} aşağı taşı`} disabled={index === siblingsCount - 1} onClick={() => reorder(c, 1)}><ArrowDown size={16} aria-hidden /></button>
                  <button type="button" className="icon-btn" aria-label={`${c.name} düzenle`} onClick={() => setEditing(editing === c.id ? undefined : c.id)}><Pencil size={16} aria-hidden /></button>
                </span>
              </li>
            )
          })}
        </ul>
      )}

      {editingCategory && (
        <CategoryEditor key={editingCategory.id} category={editingCategory} all={categories} onClose={() => setEditing(undefined)}
          onChanged={(text) => { setEditing(undefined); setMessage({ ok: true, text }); void load() }} onError={(text) => setMessage({ ok: false, text })} />
      )}
    </section>
  )
}
