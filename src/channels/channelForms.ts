/** Bağlantı formunun alan tanımları kanala göre değişir; form bu veriden çizilir. */
export type FieldKind = 'text' | 'secret' | 'environment'

export interface FieldDef {
  name: string
  label: string
  kind: FieldKind
  hint?: string
}

export interface ChannelFormDef {
  channel: string
  fields: FieldDef[]
  /** Kanalın User-Agent başlığı satıcı kimliğiyle kurulur; alanın adı (yoksa önizleme gösterilmez). */
  userAgentField?: string
}

/** Entegratör (bizim) firma adı: kanallar User-Agent'ta alfanumerik, en fazla 30 karakter ister. */
export const INTEGRATOR_NAME = 'PrimeviaSoft'

const environment: FieldDef = { name: 'environment', label: 'Ortam', kind: 'environment', hint: 'Test ortamı için kanalın test hesabı gerekir.' }

export const channelForms: ChannelFormDef[] = [
  {
    channel: 'Trendyol',
    userAgentField: 'sellerId',
    fields: [
      { name: 'sellerId', label: 'Satıcı ID', kind: 'text', hint: 'Satıcı panelinde Hesap Bilgilerim → Entegrasyon Bilgileri sayfasında bulunur.' },
      { name: 'apiKey', label: 'API Key', kind: 'secret' },
      { name: 'apiSecret', label: 'API Secret', kind: 'secret' },
      environment,
    ],
  },
  {
    channel: 'Hepsiburada',
    userAgentField: 'merchantId',
    fields: [
      { name: 'merchantId', label: 'Merchant ID', kind: 'text', hint: 'Hepsiburada Satıcı Portalı → Entegrasyon sayfasındaki GUID.' },
      { name: 'serviceKey', label: 'Servis anahtarı', kind: 'secret' },
      environment,
    ],
  },
  {
    channel: 'N11',
    fields: [
      { name: 'appKey', label: 'appKey', kind: 'secret' },
      { name: 'appSecret', label: 'appSecret', kind: 'secret' },
    ],
  },
]

export const formFor = (channel: string) => channelForms.find((f) => f.channel === channel)

/** Alanları boş olan zorunlu alanlar için hata metni döner. */
export function validate(def: ChannelFormDef, values: Record<string, string>): Record<string, string> {
  const errors: Record<string, string> = {}
  for (const f of def.fields) {
    if (f.kind === 'environment') continue
    if (!values[f.name]?.trim()) errors[f.name] = `${f.label} zorunlu.`
  }
  return errors
}

/** "{kimlik} - {firma}" biçimindeki User-Agent önizlemesi. */
export function userAgentPreview(def: ChannelFormDef, values: Record<string, string>): string | null {
  if (!def.userAgentField) return null
  const id = values[def.userAgentField]?.trim() || `{${def.fields.find((f) => f.name === def.userAgentField)?.label ?? 'Kimlik'}}`
  return `${id} - ${INTEGRATOR_NAME}`
}
