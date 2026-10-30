import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { CatalogStatus } from '../api/types'
import { Button, StatusBadge, Tabs } from '../components'
import CategoryMappingSection from './CategoryMappingSection'
import { timeAgo } from '../lib/timeAgo'
import { kindLabel, runSummary } from './catalogRuns'
import './ChannelForm.css'
import './Mapping.css'

const CHANNEL_TABS = [
  { value: 'Trendyol', label: 'Trendyol' },
  { value: 'Hepsiburada', label: 'Hepsiburada' },
  { value: 'N11', label: 'N11' },
]

export default function Mapping() {
  const [channel, setChannel] = useState('Trendyol')
  const [catalogs, setCatalogs] = useState<CatalogStatus[]>()
  const [error, setError] = useState<string>()
  const [notice, setNotice] = useState<{ ok: boolean; text: string }>()
  const [busy, setBusy] = useState<string>()

  const load = () => api<CatalogStatus[]>('/catalog').then(setCatalogs).catch((e: Error) => setError(e.message))
  useEffect(() => { load() }, [])

  // Senkron sürerken durumu arada yenile.
  const inFlight = catalogs?.some((c) => c.syncInFlight)
  useEffect(() => {
    if (!inFlight) return
    const t = setInterval(load, 10000)
    return () => clearInterval(t)
  }, [inFlight])

  async function sync(channel: string) {
    setBusy(channel)
    setNotice(undefined)
    try {
      const r = await api<{ enqueued: boolean }>(`/catalog/${encodeURIComponent(channel)}/sync`, { method: 'POST' })
      setNotice({ ok: true, text: r.enqueued ? `${channel} kataloğu senkronu kuyruğa alındı.` : `${channel} için senkron zaten sırada.` })
      await load()
    } catch (e) {
      setNotice({ ok: false, text: e instanceof Error ? e.message : 'Senkron başlatılamadı.' })
    } finally {
      setBusy(undefined)
    }
  }

  if (error) return <p role="alert" style={{ color: 'var(--danger)' }}>{error}</p>
  if (!catalogs) return <p>Yükleniyor…</p>

  return (
    <div className="mapping">
      <Tabs items={CHANNEL_TABS} value={channel} onChange={setChannel} />
      {!catalogs.some((c) => c.channel === channel) && (
        <section className="ps-card"><p className="channel-form__note">{channel} için eşleme henüz desteklenmiyor; kanal adapter'ı eklendiğinde burada açılacak.</p></section>
      )}
      {notice && <p role={notice.ok ? 'status' : 'alert'} className={notice.ok ? 'channel-form__ok' : 'channel-form__error'}>{notice.text}</p>}
      {catalogs.filter((c) => c.channel === channel).map((c) => (
        <section key={c.channel} className="ps-card mapping-card" aria-labelledby={`cat-${c.channel}`}>
          <header className="mapping-card__head">
            <h2 id={`cat-${c.channel}`}>{c.channel} kanal kataloğu</h2>
            {c.syncInFlight ? <StatusBadge variant="info">Senkron sürüyor</StatusBadge>
              : c.categories === 0 ? <StatusBadge variant="warning">Henüz çekilmedi</StatusBadge>
              : <StatusBadge variant="success">Güncel</StatusBadge>}
          </header>
          <dl className="mapping-card__stats">
            <div><dt>Kategori</dt><dd>{c.categories.toLocaleString('tr-TR')}</dd></div>
            <div><dt>Eşlenebilir (alt seviye)</dt><dd>{c.leafCategories.toLocaleString('tr-TR')}</dd></div>
            <div><dt>Marka</dt><dd>{c.brands.toLocaleString('tr-TR')}</dd></div>
            <div><dt>Özellikleri çekilen kategori</dt><dd>{c.attributeCategories.toLocaleString('tr-TR')}</dd></div>
          </dl>
          {c.lastRuns.length > 0 && (
            <ul className="mapping-card__runs" aria-label="Son senkronlar">
              {c.lastRuns.map((r) => (
                <li key={r.kind}>
                  <strong>{kindLabel[r.kind]}</strong>
                  <span>{runSummary(r)}</span>
                  <span className="mapping-card__when">{r.finishedAt ? timeAgo(r.finishedAt) : ''}</span>
                </li>
              ))}
            </ul>
          )}
          <p className="channel-form__note">
            Katalog haftada bir kendiliğinden yenilenir. Özellikler ve değerler yalnızca ürünlere eşlenmiş kategoriler için çekilir.
            Kanal bağlı değilse önce <Link to="/magazalar">Mağazalar</Link> ekranından bağlayın.
          </p>
          <div>
            <Button onClick={() => sync(c.channel)} disabled={busy === c.channel || c.syncInFlight}>
              {busy === c.channel ? 'Kuyruğa alınıyor…' : 'Kataloğu şimdi senkronla'}
            </Button>
          </div>
        </section>
      ))}
      {catalogs.some((c) => c.channel === channel) && <CategoryMappingSection channel={channel} />}
    </div>
  )
}
