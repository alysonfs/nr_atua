import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

export default defineConfig(({ command }) => ({
  plugins: [react(), tailwindcss()],
  // Em dev (`vite serve`) usa base '/' para não quebrar o servidor local.
  // Em build (`vite build`) usa '/tecnica/' para que os assets gerados
  // referenciem '/tecnica/assets/...' — caminho real no bucket S3.
  base: command === 'serve' ? '/' : '/tecnica/',
}))
