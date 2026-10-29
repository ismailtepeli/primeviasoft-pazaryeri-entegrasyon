import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Geliştirmede API aynı origin'den çağrılır; CORS gerekmez.
  server: { proxy: { '/api': 'http://localhost:5284' } },
})
