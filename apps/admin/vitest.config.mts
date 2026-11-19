import { fileURLToPath } from 'node:url';

import { defineConfig } from 'vitest/config';

// No @vitejs/plugin-react: Vite 8 compiles TSX itself, and the plugin's optional Babel 8 peers conflict with the
// Babel 7 that shadcn needs (see docs/decisions.md).
export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      // The real package throws outside a React Server Components build; tests import server modules directly.
      'server-only': fileURLToPath(new URL('./src/test/empty-module.ts', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
