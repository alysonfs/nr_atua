import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

export default defineConfig(() => ({
  plugins: [react(), tailwindcss()],
  // Base '/' tanto em dev quanto em build: a landing é publicada na raiz do
  // domínio próprio (atyno.com.br), não mais sob um prefixo de path
  // compartilhado com os demais apps. Os assets gerados referenciam
  // '/assets/...', caminho real na raiz do domínio.
  base: '/',
  // Porta fixa por app para evitar que o Vite auto-incremente e confunda
  // configuração de CORS/API entre os frontends do monorepo.
  server: { port: 5174, strictPort: true },
}))
