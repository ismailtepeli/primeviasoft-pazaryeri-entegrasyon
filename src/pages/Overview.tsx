import ChannelPanel from './ChannelPanel'
import SyncErrorPanel from './SyncErrorPanel'

export default function Overview() {
  return (
    <div className="overview">
      <ChannelPanel />
      <SyncErrorPanel />
    </div>
  )
}
