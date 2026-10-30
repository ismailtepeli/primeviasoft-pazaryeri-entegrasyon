import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import type { Category, ProductDetail, ProductImage, ProductInput } from '../api/types'
import { Button, Select, TextInput } from '../components'
import ImageManager from './ImageManager'
import { emptyForm, toInput, validateForm, type ProductForm } from './productForm'
import './ChannelForm.css'
import './ProductEdit.css'

export default function ProductEdit() {
  const { id } = useParams()
  const navigate = useNavigate()
  const isNew = !id
  const [form, setForm] = useState<ProductForm>(emptyForm)
  const [images, setImages] = useState<ProductImage[]>([])
  const [categories, setCategories] = useState<Category[]>([])
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [message, setMessage] = useState<{ ok: boolean; text: string }>()
  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [newCategory, setNewCategory] = useState('')
  const [newCategoryParent, setNewCategoryParent] = useState('')

  const set = (key: keyof ProductForm) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [key]: e.target.value }))

  useEffect(() => {
    api<Category[]>('/categories').then(setCategories).catch((e: Error) => setMessage({ ok: false, text: e.message }))
  }, [])

  useEffect(() => {
    if (isNew) { setForm(emptyForm); setImages([]); setLoading(false); return }
    setLoading(true)
    api<ProductDetail>(`/products/${id}`)
      .then((p) => {
        setForm({
          name: p.name, barcode: p.barcode, mainId: p.mainId ?? '', variant: p.variant ?? '', brand: p.brand ?? '',
          description: p.description ?? '', categoryId: p.categoryId ?? '', stock: String(p.stock), price: String(p.price),
        })
        setImages(p.images)
      })
      .catch((e: Error) => setMessage({ ok: false, text: e.message }))
      .finally(() => setLoading(false))
  }, [id, isNew])

  async function save(e: FormEvent) {
    e.preventDefault()
    const found = validateForm(form)
    setErrors(found)
    if (Object.keys(found).length > 0) return
    setSaving(true)
    setMessage(undefined)
    try {
      const body: ProductInput = toInput(form)
      const saved = await api<ProductDetail>(isNew ? '/products' : `/products/${id}`, { method: isNew ? 'POST' : 'PUT', body: JSON.stringify(body) })
      if (isNew) navigate(`/urunler/${saved.id}`, { replace: true, state: { created: true } })
      else setMessage({ ok: true, text: 'Ürün kaydedildi.' })
    } catch (err) {
      setMessage({ ok: false, text: err instanceof Error ? err.message : 'Kaydedilemedi.' })
    } finally {
      setSaving(false)
    }
  }

  async function addCategory() {
    try {
      const created = await api<Category>('/categories', { method: 'POST', body: JSON.stringify({ name: newCategory, parentId: newCategoryParent || null }) })
      setCategories(await api<Category[]>('/categories'))
      setForm((f) => ({ ...f, categoryId: created.id }))
      setNewCategory('')
      setMessage({ ok: true, text: `"${created.path}" kategorisi eklendi ve seçildi.` })
    } catch (err) {
      setMessage({ ok: false, text: err instanceof Error ? err.message : 'Kategori eklenemedi.' })
    }
  }

  if (loading) return <p>Yükleniyor…</p>

  return (
    <div className="product-edit">
      <form className="ps-card product-edit__form" onSubmit={save} noValidate aria-label="Ürün bilgileri">
        <h2>Ürün bilgileri</h2>
        <div className="product-edit__grid">
          <TextInput label="Ürün adı" value={form.name} onChange={set('name')} error={errors.name} />
          <TextInput label="Barkod" value={form.barcode} onChange={set('barcode')} error={errors.barcode} />
          <TextInput label="Ürün ana kodu" hint="Aynı ana kodlu ürünler varyant olarak gruplanır." value={form.mainId} onChange={set('mainId')} error={errors.mainId} />
          <TextInput label="Varyant" hint="Örn. Beden: M, Renk: Mavi" value={form.variant} onChange={set('variant')} error={errors.variant} />
          <TextInput label="Marka" value={form.brand} onChange={set('brand')} error={errors.brand} />
          <Select label="Kategori" value={form.categoryId} onChange={set('categoryId')}>
            <option value="">Kategorisiz</option>
            {categories.map((c) => <option key={c.id} value={c.id}>{c.path}</option>)}
          </Select>
          <TextInput label="Stok" type="number" min={0} step={1} value={form.stock} onChange={set('stock')} error={errors.stock} />
          <TextInput label="Fiyat (₺)" type="number" min={0} step="0.01" value={form.price} onChange={set('price')} error={errors.price} />
        </div>

        <div className="field">
          <label className="field__label" htmlFor="product-desc">Açıklama</label>
          <textarea id="product-desc" className="ps-input" rows={5} value={form.description} onChange={set('description')} aria-invalid={!!errors.description} />
          {errors.description && <span className="field__error">{errors.description}</span>}
        </div>

        <fieldset className="product-edit__newcat">
          <legend>Yeni kategori ekle</legend>
          <TextInput label="Kategori adı" value={newCategory} onChange={(e) => setNewCategory(e.target.value)} />
          <Select label="Üst kategori" value={newCategoryParent} onChange={(e) => setNewCategoryParent(e.target.value)}>
            <option value="">Ana kategori</option>
            {categories.map((c) => <option key={c.id} value={c.id}>{c.path}</option>)}
          </Select>
          <Button onClick={addCategory} disabled={!newCategory.trim()}>Kategori ekle</Button>
        </fieldset>

        {message && <p className={message.ok ? 'channel-form__ok' : 'channel-form__error'} role={message.ok ? 'status' : 'alert'}>{message.text}</p>}
        <div className="channel-form__actions">
          <Button variant="primary" type="submit" disabled={saving}>{saving ? 'Kaydediliyor…' : 'Kaydet'}</Button>
        </div>
      </form>

      {isNew
        ? <section className="ps-card"><p className="channel-form__note">Görsel eklemek için önce ürünü kaydedin.</p></section>
        : <ImageManager productId={id!} images={images} onChange={setImages} />}
    </div>
  )
}
