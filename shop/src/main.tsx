import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { applyTheme, readPreference } from './theme'

applyTheme(readPreference()) // ilk boyamadan önce: açık tema varsayılan, sistem karanlığı yalnızca "Sistem" seçilirse

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
