import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { AttributeMappingList, AttributeRow, CategoryMappingList } from '../api/types'
import { Button, DataTable, StatusBadge, type Column } from '../components'
import AttributeEditor from './AttributeEditor'
import { attributeStatus, mappingSummary } from './attributeMapping'
import './ChannelForm.css'

/** Kanal kategorisine eşlenmiş kendi kategorilerimiz için kanal özelliklerinin (Renk, Beden…) ve değerlerinin eşlemesi. */
export default function AttributeMappingSection({ channel }: { channel: string }) {
  const [categories, setCategories] = useState<CategoryMappingList>()
  const [categoryId, setCategoryId] = useState('')
  const [data, setData] = useState<AttributeMappingList>()
  const [editing, setEditing] = useState<AttributeRow>()
  const [error, setError] = useState<string>()
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    api<CategoryMappingList>(`/mappings/categories?channel=${encodeURIComponent(channel)}`)
      .then((c) => {
        setCategories(c)
        const first = c.rows.find((r) => r.mapped?.isActive)
        setCategoryId((current) => current || first?.categoryId || '')
      })
      .catch((e: Error) => setError(e.message))
  }, [channel])

  const load = useCallback(() => {
    if (!categoryId) { setData(undefined); return }
    api<AttributeMappingList>(`/mappings/attributes?channel=${encodeURIComponent(channel)}&categoryId=${categoryId}`)
      .then((d) => { setData(d); setError(undefined) })
      .catch((e: Error) => setError(e.message))
  }, [channel, categoryId])
  useEffect(() => { setEditing(undefined); setSaved(false); load() }, [load])

  const missingRequired = data?.rows.filter((r) => r.required && r.status !== 'Mapped') ?? []
  const mappedCategories = categories?.rows.filter((r) => r.mapped?.isActive) ?? []

  const columns: Column<AttributeRow>[] = [
    { key: 'name', header: 'Kanal özelliği', render: (r) => <>{r.name}{r.required && <span aria-label="zorunlu" title="Zorunlu"> *</span>}</> },
    { key: 'kind', header: 'Değer', render: (r) => (r.allowCustom ? 'Liste + serbest metin' : `Liste (${r.valueCount})`) },
    { key: 'how', header: 'Nasıl doluyor', render: mappingSummary },
    { key: 'status', header: 'Durum', render: (r) => { const s = attributeStatus[r.status]; return <StatusBadge variant={s.variant}>{s.text}</StatusBadge> } },
    { key: 'action', header: '', render: (r) => <Button onClick={() => setEditing(r)}>{r.mapping ? 'Düzenle' : 'Eşle'}</Button> },
  ]

  if (!categories) return error ? <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p> : null

  return (
    <section className="mapping-section" aria-labelledby="attrmap-title">
      <header className="mapping-section__head">
        <h2 id="attrmap-title">Özellik ve değer eşleme</h2>
        {data && data.requiredTotal > 0 && <span>{data.requiredMapped} / {data.requiredTotal} zorunlu özellik eşlendi</span>}
      </header>

      {mappedCategories.length === 0 ? (
        <p className="channel-form__note">Özellikleri eşlemek için önce yukarıdan en az bir kategoriyi {channel} kategorisiyle eşleyin.</p>
      ) : (
        <div className="field">
          <label className="field__label" htmlFor="attr-category">Kategori</label>
          <select id="attr-category" className="ps-input" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
            {mappedCategories.map((c) => <option key={c.categoryId} value={c.categoryId}>{c.path} → {c.mapped!.path}</option>)}
          </select>
        </div>
      )}

      {error && <p className="channel-form__error" role="alert">{error}</p>}
      {data?.needsSync && (
        <p className="channel-form__error" role="alert">
          Bu kanal kategorisinin özellikleri henüz çekilmedi. Yukarıdaki "Kataloğu şimdi senkronla" ile çekin; birkaç dakika sürebilir.
        </p>
      )}
      {saved && data && missingRequired.length === 0 && <p className="channel-form__ok" role="status">Eşleme kaydedildi; tüm zorunlu özellikler eşli.</p>}
      {data && missingRequired.length > 0 && (
        <p className="channel-form__error" role="alert">
          {saved && 'Eşleme kaydedildi, ancak '}Eksik zorunlu özellik: {missingRequired.map((r) => r.name).join(', ')}. Bu kategorideki ürünler eksik özellik nedeniyle {channel}'a gönderilmez; Ürünler ekranında "Eksik" görünür.
        </p>
      )}

      {editing && data?.channelCategory && (
        <AttributeEditor key={editing.attributeId} channel={channel} categoryId={categoryId} categoryExternalId={data.channelCategory.externalId}
          row={editing} sources={data.sources} onClose={() => setEditing(undefined)} onSaved={() => { setEditing(undefined); setSaved(true); load() }} />
      )}

      {data && data.rows.length > 0 && <DataTable columns={columns} rows={data.rows} rowKey={(r) => String(r.attributeId)} pageSize={15} />}
      <p className="channel-form__note">* zorunlu özellik. "Ürünün kendi özelliğinden" yöntemi, ürünün Varyant alanındaki "Renk: Mavi, Beden: M" gibi değerleri kullanır.</p>
    </section>
  )
}
