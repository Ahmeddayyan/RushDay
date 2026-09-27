import { fileURLToPath, URL } from 'node:url'

import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The ASP.NET Core API. In development Vite serves the SPA on 5173 and proxies API traffic here;
// in production the API serves the built SPA itself from wwwroot, so there is one origin and no CORS.
const apiTarget = 'http://localhost:5080'

// https://vite.dev/config/
export default defineConfig({
  base: '/',
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    // The API project serves this folder. It is build output: git-ignored and produced by `npm run build`.
    outDir: '../RushDay.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': apiTarget,
      '/health': apiTarget,
      '/openapi': apiTarget,
    },
  },
})
