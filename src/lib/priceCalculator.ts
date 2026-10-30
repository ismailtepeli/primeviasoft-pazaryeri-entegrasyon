export type PriceRounding = 'None' | 'NearestWhole' | 'UpWhole' | 'Ending90' | 'Ending99'

export const ROUNDING_LABELS: Record<PriceRounding, string> = {
  None: 'Yuvarlama yok (kuruşa)',
  NearestWhole: 'En yakın tam lira',
  UpWhole: 'Yukarı doğru tam lira',
  Ending90: 'Yukarı doğru ,90 ile bitsin',
  Ending99: 'Yukarı doğru ,99 ile bitsin',
}

export interface PriceRule {
  commissionPercent: number
  shippingCost: number
  profitPercent: number
  rounding: PriceRounding
}

export interface PriceBreakdown {
  base: number
  withProfit: number
  beforeRounding: number
  price: number
  commissionAmount: number
  netProfit: number
}

export const MAX_COMMISSION = 99.99
export const MAX_PROFIT = 1000
export const MAX_SHIPPING = 100000

// Kayan nokta hatalarının (örn. 222.90000000000003) sınır durumlarını bozmaması için küçük bir tolerans.
const EPS = 1e-9
const money = (v: number) => Math.round((v + EPS) * 100) / 100

/** Sunucudaki PriceCalculator ile aynı kurallar; aynı test vektörleriyle denenir. */
export function roundPrice(value: number, rounding: PriceRounding): number {
  const upToEnding = (ending: number) => {
    const candidate = Math.floor(value + EPS) + ending
    return value <= candidate + EPS ? money(candidate) : money(candidate + 1)
  }
  switch (rounding) {
    case 'NearestWhole': return Math.round(value + EPS)
    case 'UpWhole': return Math.ceil(value - EPS)
    case 'Ending90': return upToEnding(0.9)
    case 'Ending99': return upToEnding(0.99)
    default: return money(value)
  }
}

/** Kural değerleri geçersizse hata metni; geçerliyse null. */
export function validateRule(r: Pick<PriceRule, 'commissionPercent' | 'shippingCost' | 'profitPercent'>): string | null {
  if (!(r.commissionPercent >= 0 && r.commissionPercent <= MAX_COMMISSION)) return `Komisyon 0 ile ${MAX_COMMISSION} arasında olmalı.`
  if (!(r.profitPercent >= 0 && r.profitPercent <= MAX_PROFIT)) return `Kâr 0 ile ${MAX_PROFIT} arasında olmalı.`
  if (!(r.shippingCost >= 0 && r.shippingCost <= MAX_SHIPPING)) return 'Kargo maliyeti negatif ya da çok büyük olamaz.'
  return null
}

/** (maliyet + kargo) × (1 + kâr) ÷ (1 − komisyon), ardından yuvarlama. Geçersiz girdide null. */
export function calculatePrice(cost: number, rule: PriceRule): PriceBreakdown | null {
  if (!(cost > 0) || validateRule(rule)) return null
  const base = cost + rule.shippingCost
  const withProfit = base * (1 + rule.profitPercent / 100)
  const before = withProfit / (1 - rule.commissionPercent / 100)
  const price = roundPrice(before, rule.rounding)
  const commissionAmount = money(price * rule.commissionPercent / 100)
  return { base: money(base), withProfit: money(withProfit), beforeRounding: money(before), price, commissionAmount, netProfit: money(price - commissionAmount - base) }
}
