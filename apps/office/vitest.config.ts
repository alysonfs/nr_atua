import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/test/setup.ts',
    // 10s: margem de segurança sob carga de CPU (suíte completa em paralelo),
    // evitando flakiness de timeout em testes assíncronos legítimos.
    testTimeout: 10000,
  },
})
