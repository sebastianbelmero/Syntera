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
      // All API calls go through Vite's dev proxy → /api → http://localhost:5296
      // This keeps cookies/CORS simple in dev and lets the React app
      // hardcode /api/* in code without knowing the backend port.
      //
      // COOKIE HANDLING (H7 security model):
      // The backend sets an httpOnly refresh cookie on /api/auth/* with
      // no explicit Domain (host-only). Without `cookieDomainRewrite`,
      // the Set-Cookie header from the backend (origin localhost:5296)
      // passes through as-is — the browser stores it for localhost:5173
      // (the origin in the address bar), which is what we want.
      //
      // `changeOrigin: true` rewrites the Host header to localhost:5296
      // (the target) so the backend accepts the request — this is
      // independent of cookie domain handling.
      //
      // `cookieSecureRewrite` is needed when the backend sets `Secure=true`
      // (Production) but the dev proxy is HTTP. In Development we already
      // set Secure=false on the backend, so this rewrite is a no-op there,
      // but it doesn't hurt to be explicit.
      '/api': {
        target: 'http://localhost:5296',
        changeOrigin: true,
        // Preserve Set-Cookie Domain as-is (no rewrite needed — backend
        // doesn't set Domain, so cookies are host-only to localhost:5173).
        // cookieDomainRewrite is omitted on purpose.
        // cookieSecureRewrite ensures `Secure` cookies set by backend work
        // over the HTTP dev proxy (rewrites Secure=true → Secure=false).
        cookieSecureRewrite: true,
        // Note: rewrite is NOT applied — the .NET routes are already
        // prefixed with /api/, so we want the full path preserved.
      },
    },
  },
})
