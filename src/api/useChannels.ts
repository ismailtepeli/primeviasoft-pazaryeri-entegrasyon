import { useEffect, useState } from 'react'
import { api } from './client'
import type { ChannelStatus } from './types'

/** Kanal listesi ve bağlantı durumları; mağazalar ve genel bakış aynı uç noktayı kullanır. */
export function useChannels() {
  const [channels, setChannels] = useState<ChannelStatus[]>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    api<ChannelStatus[]>('/dashboard/channels')
      .then(setChannels)
      .catch((e: Error) => setError(e.message))
  }, [])

  return { channels, error }
}
