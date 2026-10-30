import type { ChannelStatus, ProductSendResult } from '../api/types'

/** Ürün gönderiminin uygulandığı kanallar; diğerleri için adapter henüz yok. */
export const SENDABLE_CHANNELS = ['Trendyol']

/** Hem gönderimi desteklenen hem de doğrulanmış (bağlı) kanallar. */
export function sendableChannels(channels: ChannelStatus[]): ChannelStatus[] {
  return channels.filter((c) => c.status === 'Connected' && SENDABLE_CHANNELS.includes(c.channel))
}

export function describeSend(channel: string, r: ProductSendResult): string {
  const parts = [`${r.products} ürün ${channel} için kuyruğa alındı`]
  if (r.unknown > 0) parts.push(`${r.unknown} ürün bulunamadı`)
  return parts.join(', ') + '.'
}
