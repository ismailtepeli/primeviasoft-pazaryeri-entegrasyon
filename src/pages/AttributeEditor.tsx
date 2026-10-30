import { useState } from 'react'
import { api } from '../api/client'
import type { AttributeRow, SourceAttribute } from '../api/types'
import { Button, TextInput } from '../components'
import { buildSaveBody, initialEditor, type EditorState } from './attributeMapping'
import ValuePicker from './ValuePicker'
import './ChannelForm.css'

interface Props {
  channel: string
  categoryId: string
  categoryExternalId: number
  row: AttributeRow
  sources: SourceAttribute[]
  onSaved: () => void
  onClose: () => void
}

/** Tek bir kanal özelliğinin nasıl doldurulacağı: tüm ürünler için sabit değer ya da ürünün kendi özelliğinden değer eşlemesi. */
export default function AttributeEditor({ channel, categoryId, categoryExternalId, row, sources, onSaved, onClose }: Props) {
  const sourceValues = (name: string) => sources.find((s) => s.name.toLowerCase() === name.toLowerCase())?.values ?? []
  const [state, setState] = useState<EditorState>(() => initialEditor(row, sourceValues))
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  const patch = (p: Partial<EditorState>) => setState((s) => ({ ...s, ...p }))

  function chooseSource(name: string) {
    // Kaynak değişince ürünlerde gördüğümüz değerler satır olarak gelir; önceki seçimler korunur.
    const values: EditorState['values'] = {}
    for (const v of sourceValues(name)) values[v] = state.values[v] ?? { externalValueId: null, customValue: '' }
    patch({ sourceAttribute: name, values })
  }

  const setValue = (v: string, change: Partial<EditorState['values'][string]>) =>
    setState((s) => ({ ...s, values: { ...s.values, [v]: { ...s.values[v], ...change } } }))

  async function save() {
    const body = buildSaveBody(channel, categoryId, row, state)
    if (typeof body === 'string') { setError(body); return }
    setBusy(true)
    setError(undefined)
    try {
      await api('/mappings/attributes', { method: 'PUT', body: JSON.stringify(body) })
      onSaved()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Kaydedilemedi.')
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    setBusy(true)
    try {
      await api(`/mappings/attributes?channel=${encodeURIComponent(channel)}&categoryId=${categoryId}&externalAttributeId=${row.attributeId}`, { method: 'DELETE' })
      onSaved()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Kaldırılamadı.')
      setBusy(false)
    }
  }

  return (
    <section className="ps-card attr-editor" aria-labelledby="attr-editor-title">
      <h2 id="attr-editor-title">{row.name}{row.required ? ' *' : ''} nasıl dolsun?</h2>
      <p className="channel-form__note">
        {row.allowCustom ? 'Bu özellik listeden değer ya da serbest metin kabul eder.' : 'Bu özellik yalnızca kanalın değer listesinden seçilebilir.'}
      </p>

      <div role="radiogroup" aria-label="Doldurma yöntemi" className="attr-editor__modes">
        <label><input type="radio" name="mode" checked={state.mode === 'fixed'} onChange={() => patch({ mode: 'fixed' })} /> Tüm ürünler için sabit değer</label>
        <label><input type="radio" name="mode" checked={state.mode === 'source'} onChange={() => patch({ mode: 'source' })} /> Ürünün kendi özelliğinden (değer eşlemeli)</label>
      </div>

      {state.mode === 'fixed' && (
        <div className="attr-editor__fixed">
          <ValuePicker channel={channel} categoryExternalId={categoryExternalId} attributeId={row.attributeId} label={`${row.name} değeri`}
            value={state.fixedValueId} valueName={row.mapping?.fixedValueName}
            onChange={(id) => patch({ fixedValueId: id, fixedCustomValue: id === null ? state.fixedCustomValue : '' })} />
          {row.allowCustom && (
            <TextInput label="ya da serbest metin" value={state.fixedCustomValue} disabled={state.fixedValueId !== null}
              onChange={(e) => patch({ fixedCustomValue: e.target.value })} />
          )}
        </div>
      )}

      {state.mode === 'source' && (
        <div className="attr-editor__source">
          <label className="field__label" htmlFor="attr-source">Ürünün özelliği</label>
          <select id="attr-source" className="ps-input" value={state.sourceAttribute} onChange={(e) => chooseSource(e.target.value)}>
            <option value="">Seçin…</option>
            {sources.map((s) => <option key={s.name} value={s.name}>{s.name}</option>)}
            {state.sourceAttribute && !sources.some((s) => s.name.toLowerCase() === state.sourceAttribute.toLowerCase()) && (
              <option value={state.sourceAttribute}>{state.sourceAttribute}</option>
            )}
          </select>
          {sources.length === 0 && (
            <p className="channel-form__note">Bu kategorideki ürünlerin Varyant alanında "Renk: Mavi" biçiminde özellik yok; önce ürünlere ekleyin ya da sabit değer kullanın.</p>
          )}
          {Object.keys(state.values).length > 0 && (
            <table className="table attr-editor__values">
              <thead><tr><th>Ürünündeki değer</th><th>{channel} değeri</th>{row.allowCustom && <th>ya da serbest metin</th>}</tr></thead>
              <tbody>
                {Object.entries(state.values).map(([v, sel]) => (
                  <tr key={v}>
                    <td>{v}</td>
                    <td>
                      <ValuePicker channel={channel} categoryExternalId={categoryExternalId} attributeId={row.attributeId} label={`${v} için ${row.name} değeri`}
                        value={sel.externalValueId} valueName={row.mapping?.values.find((x) => x.sourceValue === v)?.externalValueName}
                        onChange={(id) => setValue(v, { externalValueId: id, customValue: id === null ? sel.customValue : '' })} />
                    </td>
                    {row.allowCustom && (
                      <td><input className="ps-input" aria-label={`${v} için serbest metin`} value={sel.customValue} disabled={sel.externalValueId !== null}
                        onChange={(e) => setValue(v, { customValue: e.target.value })} /></td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {error && <p className="channel-form__error" role="alert">{error}</p>}
      <div className="channel-form__actions">
        {row.mapping && <Button onClick={remove} disabled={busy}>Eşlemeyi kaldır</Button>}
        <Button onClick={onClose}>Vazgeç</Button>
        <Button onClick={save} disabled={busy}>{busy ? 'Kaydediliyor…' : 'Kaydet'}</Button>
      </div>
    </section>
  )
}
