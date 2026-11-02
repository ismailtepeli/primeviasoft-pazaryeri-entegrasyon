import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// B2C mağaza (herkese açık vitrin), yönetim panelinden ve bayi sitesinden ayrı bir uygulamadır; geliştirmede 5176 portunda çalışır ve API'yi aynı origin'den çağırır.
export default defineConfig({
  plugins: [react()],
  server: { port: 5176, proxy: { '/api': 'http://localhost:5284' } },
})
