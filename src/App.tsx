import { BrowserRouter, Route, Routes } from 'react-router-dom'
import AppLayout from './layout/AppLayout'
import PagePlaceholder from './pages/PagePlaceholder'
import { routes } from './routes'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<AppLayout />}>
          {routes.map((r) => (
            <Route key={r.path} path={r.path} element={<PagePlaceholder name={r.label} />} />
          ))}
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
