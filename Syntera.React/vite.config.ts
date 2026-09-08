import react, { reactCompilerPreset } from '@vitejs/plugin-react'
import babel from '@rolldown/plugin-babel'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    babel({ presets: [reactCompilerPreset()] }),
    tailwindcss(),
  ],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      // All API calls go through Vite's dev proxy → /api → http://localhost:5296.
      //
      // COOKIE FIX (Sprint 2.5 silent-refresh-on-reload):
      // DO NOT set changeOrigin: true. When changeOrigin is true, Vite
      // rewrites the Host header to localhost:5296 (the target). The
      // backend then sets cookies with Domain defaulting to the Host
      // header value (localhost:5296). The browser sees the response
      // coming from localhost:5173 (its address bar origin) and REJECTS
      // the cookie because localhost:5296 is NOT a match for localhost:5173.
      //
      // Without changeOrigin, the Host header stays as localhost:5173
      // (the browser's origin). The backend sets cookies with Domain
      // defaulting to localhost:5173. The browser stores the cookie
      // for localhost:5173 and sends it on subsequent requests. ✅
      //
      // The backend accepts any Host (AllowedHosts: "*") so it doesn't
      // care that Host=localhost:5173 even though it listens on port 5296.
      '/api': {
        target: 'http://localhost:5296',
        // changeOrigin: false (default) — keep Host header as localhost:5173
        // so cookie Domain matches the browser's origin.
        // Note: rewrite is NOT applied — the .NET routes are already
        // prefixed with /api/, so we want the full path preserved.
      },
    },
  },
})
