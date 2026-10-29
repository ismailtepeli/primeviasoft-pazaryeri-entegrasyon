import { useState, type ReactNode } from 'react'
import { Button } from './Button'

export interface Column<T> {
  key: string
  header: string
  render: (row: T) => ReactNode
}

interface DataTableProps<T> {
  columns: Column<T>[]
  rows: T[]
  rowKey: (row: T) => string
  selectable?: boolean
  selected?: string[]
  onSelectionChange?: (keys: string[]) => void
  pageSize?: number
  emptyTitle?: string
  emptyText?: string
}

export function DataTable<T>({
  columns, rows, rowKey, selectable, selected = [], onSelectionChange,
  pageSize = 10, emptyTitle = 'Kayıt bulunamadı', emptyText = 'Filtreleri değiştirmeyi deneyin.',
}: DataTableProps<T>) {
  const [requestedPage, setPage] = useState(0)
  const pageCount = Math.max(1, Math.ceil(rows.length / pageSize))
  const page = Math.min(requestedPage, pageCount - 1)

  const visible = rows.slice(page * pageSize, (page + 1) * pageSize)
  const visibleKeys = visible.map(rowKey)
  const allSelected = visibleKeys.length > 0 && visibleKeys.every((k) => selected.includes(k))

  const toggle = (key: string) =>
    onSelectionChange?.(selected.includes(key) ? selected.filter((k) => k !== key) : [...selected, key])
  const toggleAll = () =>
    onSelectionChange?.(allSelected
      ? selected.filter((k) => !visibleKeys.includes(k))
      : [...new Set([...selected, ...visibleKeys])])

  return (
    <div className="table-wrap">
      <div className="table-scroll">
        <table className="table">
          <thead>
            <tr>
              {selectable && (
                <th className="table__check">
                  <input type="checkbox" aria-label="Sayfadaki tümünü seç" checked={allSelected} onChange={toggleAll} />
                </th>
              )}
              {columns.map((c) => <th key={c.key}>{c.header}</th>)}
            </tr>
          </thead>
          <tbody>
            {visible.map((row) => {
              const key = rowKey(row)
              const isSelected = selected.includes(key)
              return (
                <tr key={key} aria-selected={selectable ? isSelected : undefined}>
                  {selectable && (
                    <td className="table__check">
                      <input type="checkbox" aria-label="Satırı seç" checked={isSelected} onChange={() => toggle(key)} />
                    </td>
                  )}
                  {columns.map((c) => <td key={c.key}>{c.render(row)}</td>)}
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      {rows.length === 0 && (
        <div className="table__empty"><strong>{emptyTitle}</strong>{emptyText}</div>
      )}
      {rows.length > 0 && (
        <div className="pager">
          <span>{page * pageSize + 1}–{Math.min((page + 1) * pageSize, rows.length)} / {rows.length}</span>
          <div className="pager__buttons">
            <Button disabled={page === 0} onClick={() => setPage(page - 1)}>Önceki</Button>
            <Button disabled={page >= pageCount - 1} onClick={() => setPage(page + 1)}>Sonraki</Button>
          </div>
        </div>
      )}
    </div>
  )
}
