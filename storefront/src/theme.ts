export type ThemePreference = 'light' | 'dark' | 'system'
export type Theme = 'light' | 'dark'

const KEY = 'store.theme'

export const themeOptions: { value: ThemePreference; label: string }[] = [
  { value: 'light', label: 'Açık' },
  { value: 'dark', label: 'Koyu' },
  { value: 'system', label: 'Sistem' },
]

/** Sistem tercihi yalnızca "Sistem" seçildiğinde izlenir; varsayılan açık temadır. */
export function resolveTheme(preference: ThemePreference, systemDark: boolean): Theme {
  if (preference === 'system') return systemDark ? 'dark' : 'light'
  return preference
}

export function readPreference(): ThemePreference {
  try {
    const saved = localStorage.getItem(KEY)
    if (saved === 'light' || saved === 'dark' || saved === 'system') return saved
  } catch { /* depolama kapalı olabilir */ }
  return 'light'
}

export function savePreference(preference: ThemePreference): void {
  try { localStorage.setItem(KEY, preference) } catch { /* depolama kapalı olabilir */ }
}

export function applyTheme(preference: ThemePreference): Theme {
  const systemDark = typeof window !== 'undefined' && window.matchMedia('(prefers-color-scheme: dark)').matches
  const theme = resolveTheme(preference, systemDark)
  document.documentElement.dataset.theme = theme
  return theme
}
