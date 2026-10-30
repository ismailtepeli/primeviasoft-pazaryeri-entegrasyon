import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Bayi sitesi, yönetim panelinden ayrı bir uygulamadır; geliştirmede 5174 portunda çalışır ve API'yi aynı origin'den çağırır.
export default defineConfig({
  plugins: [react()],
  server: { port: 5174, proxy: { '/api': 'http://localhost:5284' } },
})
