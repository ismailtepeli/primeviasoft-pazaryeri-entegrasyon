const tokenKey = (slug: string) => `shop.token.${slug}`

/**
 * Müşteri oturum anahtarı: "Beni hatırla" işaretliyse localStorage (tarayıcı kapansa da sürer),
 * değilse sessionStorage (sekme/pencere kapanınca biter). Okurken ikisine de bakılır; depolama kapalıysa hata fırlatmaz.
 */
export const tokenStore = {
  get: (slug: string) => { try { return sessionStorage.getItem(tokenKey(slug)) ?? localStorage.getItem(tokenKey(slug)) } catch { return null } },
  set: (slug: string, token: string, remember = true) => {
    try {
      sessionStorage.removeItem(tokenKey(slug))
      localStorage.removeItem(tokenKey(slug))
      ;(remember ? localStorage : sessionStorage).setItem(tokenKey(slug), token)
    } catch { /* depolama kapalı olabilir */ }
  },
  clear: (slug: string) => { try { sessionStorage.removeItem(tokenKey(slug)); localStorage.removeItem(tokenKey(slug)) } catch { /* depolama kapalı olabilir */ } },
}
