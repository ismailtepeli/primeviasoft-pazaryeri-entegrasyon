import { Link } from 'react-router-dom'
import { useModules } from '../modules/ModulesContext'
import './Settings.css'

/** Kapalı bir modülün adresine gidildiğinde gösterilir. */
export default function ModuleDisabled({ moduleId }: { moduleId: string }) {
  const { modules } = useModules()
  const name = modules?.find((m) => m.id === moduleId)?.name ?? moduleId
  return (
    <section className="ps-card module-disabled" aria-labelledby="module-disabled-title">
      <h2 id="module-disabled-title">{name} modülü kapalı</h2>
      <p>Bu ekranı kullanmak için modülü Ayarlar sayfasından açın. Verileriniz silinmedi.</p>
      <Link to="/ayarlar" className="ps-btn ps-btn--secondary">Ayarlar &gt; Modüller</Link>
    </section>
  )
}
