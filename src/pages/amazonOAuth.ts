/** Client secret yenileme hatırlatıcısının kullanıcıya gösterilecek metni; hatırlatma gerekmiyorsa null. */
export function secretReminderText(daysLeft: number | null, reminder: boolean): string | null {
  if (!reminder || daysLeft === null) return null
  if (daysLeft < 0) return `Amazon uygulamasının client secret süresi ${-daysLeft} gün önce doldu. Seller Central'dan yenileyip sunucu ayarını güncelleyin; yenilenene kadar yetkilendirme çalışmaz.`
  if (daysLeft === 0) return 'Amazon uygulamasının client secret süresi bugün doluyor. Seller Central\'dan yenileyin.'
  return `Amazon uygulamasının client secret süresi ${daysLeft} gün sonra doluyor. Seller Central'dan yenileyip sunucu ayarını güncelleyin.`
}
