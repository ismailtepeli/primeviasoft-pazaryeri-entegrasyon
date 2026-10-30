import type { ChannelStatus, ProductSendResult } from '../api/types'

/** Ürün gönderiminin uygulandığı kanallar; diğerleri için adapter henüz yok. */
export const SENDABLE_CHANNELS = ['Trendyol']

/** Hem gönderimi desteklenen hem de doğrulanmış (bağlı) kanallar. */
export function sendableChannels(channels: ChannelStatus[]): ChannelStatus[] {
  return channels.filter((c) => c.status === 'Connected' && SENDABLE_CHANNELS.includes(c.channel))
}

export function describeSend(channel: string, r: ProductSendResult): string {
  const parts: string[] = []
  parts.push(r.products > 0 ? `${r.products} ürün ${channel} için kuyruğa alındı` : `Hiçbir ürün ${channel} için kuyruğa alınmadı`)
  if (r.blocked > 0) parts.push(`${r.blocked} ürün eksik bilgi nedeniyle gönderilmedi (Ürünler listesinde "Eksik" olarak nedenleriyle görünür)`)
  if (r.unknown > 0) parts.push(`${r.unknown} ürün bulunamadı`)
  return parts.join(', ') + '.'
}

/** Gönderim sonucu hiç ürün kuyruğa alınmadıysa (hepsi eksikse) uyarı olarak gösterilir. */
export function sendWasBlocked(r: ProductSendResult): boolean {
  return r.products === 0 && r.blocked > 0
}
