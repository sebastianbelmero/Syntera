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
      // All API calls go through Vite's dev proxy → /api → http://localhost:5113
      // This keeps cookies/CORS simple in dev and lets the React app
      // hardcode /api/* in code without knowing the backend port.
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        // Note: rewrite is NOT applied — the .NET routes are already
        // prefixed with /api/, so we want the full path preserved.
      },
    },
  },
})
