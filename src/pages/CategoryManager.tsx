import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { Category } from '../api/types'
import { Button, DataTable, TextInput, type Column } from '../components'
import './ChannelForm.css'

/** Kategori listesi: yeniden adlandırma ve silme. Ekleme ürün sayfasında ya da Excel'den yapılır. */
export default function CategoryManager({ onClose }: { onClose: () => void }) {
  const [categories, setCategories] = useState<Category[]>([])
  const [editing, setEditing] = useState<string>()
  const [name, setName] = useState('')
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()

  const load = () => api<Category[]>('/categories').then(setCategories).catch((e: Error) => setMessage({ ok: false, text: e.message }))
  useEffect(() => { load() }, [])

  async function rename(c: Category) {
    try {
      await api(`/categories/${c.id}`, { method: 'PUT', body: JSON.stringify({ name }) })
      setEditing(undefined)
      setMessage({ ok: true, text: 'Kategori adı güncellendi.' })
      await load()
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Güncellenemedi.' })
    }
  }

  async function remove(c: Category) {
    try {
      await api(`/categories/${c.id}`, { method: 'DELETE' })
      setMessage({ ok: true, text: `"${c.path}" silindi.` })
      await load()
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Silinemedi.' })
    }
  }

  const columns: Column<Category>[] = [
    {
      key: 'path', header: 'Kategori',
      render: (c) => editing === c.id
        ? <TextInput label="Yeni ad" value={name} onChange={(e) => setName(e.target.value)} />
        : c.path,
    },
    { key: 'count', header: 'Ürün', render: (c) => c.productCount },
    {
      key: 'actions', header: '',
      render: (c) => editing === c.id
        ? <span className="category-actions"><Button onClick={() => rename(c)} disabled={!name.trim()}>Kaydet</Button><Button onClick={() => setEditing(undefined)}>Vazgeç</Button></span>
        : <span className="category-actions"><Button onClick={() => { setEditing(c.id); setName(c.name) }}>Adı değiştir</Button><Button onClick={() => remove(c)}>Sil</Button></span>,
    },
  ]

  return (
    <section className="ps-card channel-form" style={{ maxWidth: 'none' }} aria-labelledby="cat-title">
      <h2 id="cat-title">Kategoriler</h2>
      {message && <p className={message.ok ? 'channel-form__ok' : 'channel-form__error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
      <DataTable columns={columns} rows={categories} rowKey={(c) => c.id} pageSize={10}
        emptyTitle="Henüz kategori yok" emptyText="Ürün sayfasından ya da Excel yüklemesiyle kategori ekleyebilirsiniz." />
      <div className="channel-form__actions"><Button onClick={onClose}>Kapat</Button></div>
    </section>
  )
}
