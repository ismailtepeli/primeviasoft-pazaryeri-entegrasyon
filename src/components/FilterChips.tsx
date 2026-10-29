export interface ChipItem {
  value: string
  label: string
}

interface FilterChipsProps {
  items: ChipItem[]
  value: string
  onChange: (value: string) => void
}

export function FilterChips({ items, value, onChange }: FilterChipsProps) {
  return (
    <div className="chips" role="group" aria-label="Filtreler">
      {items.map((c) => (
        <button key={c.value} type="button" className="chip-btn"
          aria-pressed={c.value === value} onClick={() => onChange(c.value)}>
          {c.label}
        </button>
      ))}
    </div>
  )
}
