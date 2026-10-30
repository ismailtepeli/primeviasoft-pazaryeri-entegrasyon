import { useRef, useState } from 'react'
import { api } from '../api/client'
import type { ProductImage } from '../api/types'
import { Button, TextInput } from '../components'
import { MAX_IMAGES, imageHint, moveItem } from './imageOrder'
import './ImageManager.css'

interface Props {
  productId: string
  images: ProductImage[]
  onChange: (images: ProductImage[]) => void
}

/** Ürün görselleri: yükleme, adresle ekleme, sürükleyerek ya da düğmelerle sıralama, silme. İlk görsel kapaktır. */
export default function ImageManager({ productId, images, onChange }: Props) {
  const fileInput = useRef<HTMLInputElement>(null)
  const [url, setUrl] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const [dragFrom, setDragFrom] = useState<number>()

  const base = `/products/${productId}/images`
  const full = images.length >= MAX_IMAGES
  const hint = imageHint(images.length)

  async function run(action: () => Promise<ProductImage[]>) {
    setBusy(true)
    setError(undefined)
    try {
      onChange(await action())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'İşlem başarısız.')
    } finally {
      setBusy(false)
    }
  }

  const upload = () => run(async () => {
    const files = [...(fileInput.current?.files ?? [])]
    if (files.length === 0) throw new Error('Önce görsel dosyası seçin.')
    const body = new FormData()
    files.forEach((f) => body.append('files', f))
    const result = await api<ProductImage[]>(base, { method: 'POST', body })
    if (fileInput.current) fileInput.current.value = ''
    return result
  })

  const addUrl = () => run(async () => {
    const result = await api<ProductImage[]>(`${base}/url`, { method: 'POST', body: JSON.stringify({ url }) })
    setUrl('')
    return result
  })

  const reorder = (from: number, to: number) => {
    const next = moveItem(images, from, to)
    if (next === images) return
    // Önce ekranda göster, sunucu reddederse eski sıraya dön.
    const previous = images
    onChange(next.map((img, i) => ({ ...img, position: i })))
    setError(undefined)
    api<ProductImage[]>(`${base}/order`, { method: 'PUT', body: JSON.stringify({ ids: next.map((i) => i.id) }) })
      .then(onChange)
      .catch((e: Error) => { onChange(previous); setError(e.message) })
  }

  const remove = (id: string) => run(() => api<ProductImage[]>(`${base}/${id}`, { method: 'DELETE' }))

  return (
    <section className="ps-card image-manager" aria-labelledby="images-title">
      <header className="image-manager__head">
        <h2 id="images-title">Görseller</h2>
        <span className="image-manager__count">{images.length} / {MAX_IMAGES}</span>
      </header>
      {hint && <p className="image-manager__hint" role="status">{hint}</p>}

      {images.length > 0 && (
        <ol className="image-manager__grid" aria-label="Görsel sırası">
          {images.map((img, i) => (
            <li key={img.id} className="image-manager__item" draggable
              onDragStart={() => setDragFrom(i)}
              onDragOver={(e) => e.preventDefault()}
              onDrop={() => { if (dragFrom !== undefined) reorder(dragFrom, i); setDragFrom(undefined) }}
              onDragEnd={() => setDragFrom(undefined)}>
              <img src={img.url} alt={`Ürün görseli ${i + 1}`} loading="lazy" />
              {i === 0 && <span className="image-manager__cover">Kapak</span>}
              <div className="image-manager__tools">
                <button type="button" aria-label={`${i + 1}. görseli sola taşı`} disabled={i === 0 || busy} onClick={() => reorder(i, i - 1)}>←</button>
                <button type="button" aria-label={`${i + 1}. görseli sağa taşı`} disabled={i === images.length - 1 || busy} onClick={() => reorder(i, i + 1)}>→</button>
                {i > 0 && <button type="button" disabled={busy} onClick={() => reorder(i, 0)}>Kapak yap</button>}
                <button type="button" disabled={busy} aria-label={`${i + 1}. görseli sil`} onClick={() => remove(img.id)}>Sil</button>
              </div>
            </li>
          ))}
        </ol>
      )}

      <div className="image-manager__add">
        <div className="image-manager__upload">
          <input ref={fileInput} type="file" multiple accept="image/jpeg,image/png,image/webp" aria-label="Görsel dosyaları seç" disabled={full} />
          <Button onClick={upload} disabled={busy || full}>Yükle</Button>
        </div>
        <div className="image-manager__url">
          <TextInput label="Görsel adresi (URL)" placeholder="https://…" value={url} disabled={full}
            onChange={(e) => setUrl(e.target.value)} />
          <Button onClick={addUrl} disabled={busy || full || !url.trim()}>Adresle ekle</Button>
        </div>
      </div>
      <p className="image-manager__note">jpg, png ya da webp; dosya başına en fazla 5 MB. İlk görsel katalogda kapak olarak görünür.</p>
      {error && <p className="channel-form__error" role="alert">{error}</p>}
    </section>
  )
}
