import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

export default defineConfig(({ command }) => ({
  plugins: [react(), tailwindcss()],
  // Em dev (`vite serve`) usa base '/' para não quebrar o servidor local.
  // Em build (`vite build`) usa '/office/' para que os assets gerados
  // referenciem '/office/assets/...' — caminho real no bucket S3.
  base: command === 'serve' ? '/' : '/office/',
  // Porta fixa por app para evitar que o Vite auto-incremente e confunda
  // configuração de CORS/API entre os frontends do monorepo.
  server: { port: 5175, strictPort: true },
}))
