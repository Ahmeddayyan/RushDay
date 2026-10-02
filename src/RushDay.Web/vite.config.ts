import { fileURLToPath, URL } from 'node:url'

import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The ASP.NET Core API. In development Vite serves the SPA on 5173 and proxies API traffic here;
// in production the API serves the built SPA itself from wwwroot, so there is one origin and no CORS.
const apiTarget = process.env.VITE_API_PROXY ?? 'http://localhost:5080'

// https://vite.dev/config/
export default defineConfig({
  base: '/',
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: {
    port: 5173,
    strictPort: true,
    // changeOrigin: false keeps Host: localhost:5173, so the API's Set-Cookie lands on `localhost`
    // and the browser returns it through the proxy (02-api.md section 2.1).
    proxy: { '/api': { target: apiTarget, changeOrigin: false } },
  },
  build: {
    // The API project serves this folder. It is build output: git-ignored and produced by `npm run build`.
    outDir: '../RushDay.Api/wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    target: 'es2022',
    manifest: true,
    rolldownOptions: {
      output: {
        advancedChunks: {
          groups: [
            { name: 'react', test: /node_modules[\\/](react|react-dom|react-router|scheduler)[\\/]/ },
            { name: 'query', test: /node_modules[\\/]@tanstack[\\/]/ },
            {
              name: 'charts',
              test: /node_modules[\\/](recharts|d3-|victory-vendor|internmap|delaunator|robust-predicates)/,
            },
          ],
        },
      },
    },
  },
})
