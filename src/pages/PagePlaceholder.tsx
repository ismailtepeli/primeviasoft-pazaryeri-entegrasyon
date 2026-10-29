export default function PagePlaceholder({ name }: { name: string }) {
  return (
    <div className="ps-card">
      <p style={{ margin: 0, color: 'var(--ink-muted)' }}>{name} ekranı yakında burada olacak.</p>
    </div>
  )
}
