import { useState } from 'react'
import {
  Button, DataTable, FilterChips, KpiCard, Select, SecretInput, StatusBadge, Tabs, TextInput, ThemeToggle,
  type BadgeVariant, type Column,
} from '../components'

interface Row {
  id: string
  name: string
  status: BadgeVariant
  label: string
}

const statuses: [BadgeVariant, string][] = [
  ['success', 'Yayında'], ['warning', 'Beklemede'], ['error', 'Hatalı'], ['info', 'İşleniyor'], ['inactive', 'Pasif'],
]
const rows: Row[] = Array.from({ length: 23 }, (_, i) => {
  const [status, label] = statuses[i % statuses.length]
  return { id: String(i + 1), name: `Örnek ürün ${i + 1}`, status, label }
})
const columns: Column<Row>[] = [
  { key: 'name', header: 'Ürün', render: (r) => r.name },
  { key: 'status', header: 'Durum', render: (r) => <StatusBadge variant={r.status}>{r.label}</StatusBadge> },
]

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section style={{ display: 'grid', gap: 'var(--space-4)' }}>
      <h2 style={{ margin: 0, fontSize: 21 }}>{title}</h2>
      {children}
    </section>
  )
}

/** Bileşenlerin canlı örneği; menüde yok, /tasarim-sistemi adresinden açılır. */
export default function DesignSystem() {
  const [tab, setTab] = useState('all')
  const [chip, setChip] = useState('all')
  const [selected, setSelected] = useState<string[]>([])
  const [empty, setEmpty] = useState(false)

  return (
    <div style={{ maxWidth: 960, margin: '0 auto', padding: 'var(--space-6)', display: 'grid', gap: 'var(--space-8)' }}>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h1 style={{ margin: 0 }}>Tasarım sistemi</h1>
        <div style={{ background: 'var(--surface-inverse)', borderRadius: 999, padding: 4 }}><ThemeToggle /></div>
      </header>

      <Section title="Butonlar">
        <div style={{ display: 'flex', gap: 'var(--space-3)' }}>
          <Button variant="primary">Kaydet</Button>
          <Button>Vazgeç</Button>
          <Button disabled>Pasif</Button>
        </div>
      </Section>

      <Section title="KPI kartı">
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 'var(--space-4)' }}>
          <KpiCard label="Bugünkü sipariş" value={128} hint="Dünden %12 fazla" />
          <KpiCard label="Aktif ürün" value="4.310" />
          <KpiCard label="Hatalı gönderim" value={7} hint="Gözden geçirin" />
        </div>
      </Section>

      <Section title="Durum rozeti">
        <div style={{ display: 'flex', gap: 'var(--space-3)', flexWrap: 'wrap' }}>
          {statuses.map(([v, l]) => <StatusBadge key={v} variant={v}>{l}</StatusBadge>)}
        </div>
      </Section>

      <Section title="Sekme ve filtre chip'i">
        <Tabs value={tab} onChange={setTab} items={[
          { value: 'all', label: 'Tümü', count: 23 }, { value: 'new', label: 'Yeni', count: 5 }, { value: 'done', label: 'Tamamlanan' },
        ]} />
        <FilterChips value={chip} onChange={setChip} items={[
          { value: 'all', label: 'Tümü' }, { value: 'trendyol', label: 'Trendyol' }, { value: 'amazon', label: 'Amazon' },
        ]} />
      </Section>

      <Section title="Form">
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 'var(--space-4)' }}>
          <TextInput label="Mağaza adı" placeholder="Örn. Ana mağaza" hint="Listede bu adla görünür." />
          <SecretInput label="API anahtarı" defaultValue="gizli-anahtar" />
          <Select label="Kanal"><option>Trendyol</option><option>Amazon</option></Select>
          <TextInput label="Satıcı kimliği" error="Satıcı kimliği boş olamaz." />
        </div>
      </Section>

      <Section title="Tablo">
        <div><Button onClick={() => setEmpty(!empty)}>{empty ? 'Satırları göster' : 'Boş durumu göster'}</Button></div>
        <DataTable columns={columns} rows={empty ? [] : rows} rowKey={(r) => r.id} selectable pageSize={8}
          selected={selected} onSelectionChange={setSelected} emptyTitle="Henüz ürün yok" emptyText="İlk ürününüzü ekleyin." />
        <p style={{ margin: 0, color: 'var(--ink-muted)' }}>{selected.length} satır seçili</p>
      </Section>
    </div>
  )
}
