# Syntera.React

The React 19 single-page application for the **Syntera IAM** platform.
Built on Vite 8 + TypeScript 6 + Tailwind v4 + TanStack Query v5 + Zustand v4
+ React Router 7, with a **fully self-contained UI layer** — all primitives
(Avatar, DropdownMenu, Button) and the admin shell (AdminLayout, AppSidebar,
AppHeader) live in this repo's `src/`. No external component library is
required at runtime (Radix UI primitives are wrapped in-house).

> See the parent [../README.md](../README.md) for full architecture,
> setup, and deployment docs. This file is a quick developer reference.

## Quick start

```bash
# From this directory:
bun install                       # installs Syntera.React deps (bun.lock)
bun run dev                       # http://localhost:5173 (proxies /api to :5296)
bun run build                     # tsc -b && vite build → dist/
bun run typecheck                 # tsc --noEmit
bun run lint                      # oxlint
bun run preview                   # vite preview (preview the prod build)
```

> `npm` works too if you don't have `bun` — `npm install`, `npm run dev`,
> etc. The lockfile committed is `bun.lock`.

## Backend proxy

Vite dev server proxies `/api/*` → `http://localhost:5296` (the .NET
backend). No path rewrite — the backend routes are already prefixed with
`/api/`. See `vite.config.ts`.

The axios client hardcodes `baseURL: "/api"` (see `src/api/client.ts`).
There is **no `VITE_API_BASE_URL` env var** — production relies on a
reverse proxy mapping `/api/*` → the backend.

## Folder map

| Path | Purpose |
| --- | ------- |
| `src/api/` | Single axios instance + per-aggregate endpoint helpers (`auth`, `platform`, `site`, `audit`) |
| `src/components/ui/` | In-house Radix wrappers (Avatar, DropdownMenu, Button) |
| `src/components/layout/` | Admin shell: AdminLayout, AppSidebar, AppHeader |
| `src/lib/` | `cn()` class composer (twMerge + clsx) |
| `src/pages/` | One folder per domain: `auth`, `dashboard`, `platform`, `site`, `audit`, `settings` |
| `src/routes/` | Route guards: `RequireAuth`, `RequirePlatformAdmin`, `RequirePlatformOrSystemAdmin`, `RequireSiteAdmin`, `RequireRole` |
| `src/store/` | Zustand stores: `authStore` (in-memory tokens + persisted theme) + `themeStore` (light/dark) |
| `src/types/` | Mirror of backend DTOs (single file, `index.ts`) |
| `src/index.css` | Tailwind v4 entry + 7 brand palettes (light + dark) + animations |

## What's NOT here (intentional)

- **No `AppBreadcrumb`** — navigation feedback is handled by the sidebar's
  active link state.
- **No `DataTable`, `Modal`, `Field`** components — pages use native
  `<table>` and a custom `<Drawer>` helper (defined in
  `pages/platform/SitesPage.tsx`) for slide-in panels.
- **No `src/providers/`** folder — axios reads directly from Zustand, no
  context provider needed.
- **No `src/hooks/`** folder — mutation hooks are inlined into pages
  (intentional for visibility).
- **No `VITE_API_BASE_URL` env var** — `client.ts` hardcodes `/api`.
- **No `change-password` UI** — `SettingsPage` is read-only. (The backend
  endpoint `POST /api/auth/change-password` exists for Platform Admin via
  `curl` or Swagger.)
- **No LDAP sync UI** — manual user provisioning only. Per comment in
  `api/site.ts`: "LDAP sync is NOT available (no service account). Business
  Admin must create user rows manually before users can log in."

## Security model (H7-full, Sprint 4)

- **Access token:** in-memory only in `authStore`. Wiped on page reload.
- **Refresh token:** httpOnly cookie `syntera_refresh` (set by backend).
  JS never reads it; XSS cannot exfiltrate it.
- **Silent refresh on boot:** `main.tsx` calls `initAuth()` before
  `createRoot().render()` — pings `/api/auth/refresh` (cookie-driven).
  If the cookie is valid, the store is repopulated; otherwise the login
  page renders.
- **No login-page flash:** while `initializing=true`, all route guards
  render `<AuthInitializing />` (spinner) instead of redirecting.
- **Profile:** NOT persisted — re-fetched via silent refresh on boot
  (avoids stale role data after a role change).
- **Theme:** IS persisted to localStorage (not sensitive, needed before
  silent refresh completes to avoid FOUC on login page).

## 401 refresh interceptor

`src/api/client.ts` intercepts 401 responses (excluding `/auth/*`):

1. Marks the request as `_retry=true`.
2. Calls `acquireFreshAccessToken()` — single-flight queue prevents
   thundering herd when many requests 401 simultaneously.
3. Picks endpoint based on profile scope:
   - Platform scope → `POST /api/auth/refresh` (no body)
   - Site scope → `POST /api/auth/refresh-site { siteId }`
4. On success → `setTokens(...)` + `updateTheme(...)`, retries the
   original request.
5. On failure → `logout()` + redirect to `/login`.

## Route guards (`src/routes/guards.tsx`)

| Guard | Allowed roles |
| ----- | ------------- |
| `RequireAuth` | Any authenticated user |
| `RequirePlatformAdmin` | `platform-admin` |
| `RequirePlatformOrSystemAdmin` | `platform-admin` OR `system-admin` |
| `RequireSiteAdmin` | `platform-admin`, `site-business-admin`, `system-admin`, `eng-manager`, `supervisor`, `qo-manager` |
| `RequireRole({ roles })` | Generic, backward-compat |

## Routes (`src/App.tsx`)

| Path | Element | Guard |
| ---- | ------- | ----- |
| `/login` | `LoginPage` | — |
| `/dashboard` | `DashboardPage` | `RequireAuth` (parent) |
| `/platform/sites` | `SitesPage` | `RequirePlatformOrSystemAdmin` |
| `/platform/role-templates` | `RoleTemplatesPage` | `RequirePlatformAdmin` |
| `/site/users` | `UsersPage` | `RequireSiteAdmin` |
| `/audit/logs` & `/site/audit` | `AuditLogsPage` | `RequireAuth` (parent) |
| `/settings` | `SettingsPage` | `RequireAuth` (parent) |
| `/` and `*` | `<Navigate to="/dashboard">` | — |

## Theme application (`src/App.tsx → ThemeApplier`)

`<ThemeApplier />` runs at the App root (renders before `<Routes>`):

1. Reads `authStore.theme` (the brand palette bundle) and
   `themeStore.isDark` (user's light/dark preference).
2. Picks `palette = isDark ? theme.dark : theme.light`.
3. Writes **two** sets of CSS variables to `document.documentElement`:
   - `--color-*` (10 vars) — used by inline-style pages (IAM pages).
   - Tailwind-style vars (`--background`, `--primary`, `--card`, …) —
     used by layout + UI components.
4. Sets `data-theme="<themeKey>"` on `<html>`.
5. Toggles `.dark` class on `<html>`.

Brand palettes are defined in `src/index.css` (7 themes: `syntera-default`,
`kalbe`, `dankos`, `hexpharm`, `fima`, `gof`, `kalventis`).

## Menu builder (`src/App.tsx → buildMenu`)

The sidebar menu is role-driven:

- **Dashboard** — always shown.
- **Platform Admin:** Sites, Role Templates, Audit Logs.
- **System Admin (not Platform):** Sites (to manage Business Admins for
  their own site).
- **Site Admin OR System Admin:** Users, Site Audit.
- **Settings** — always shown.

## Conventions

- **One Axios instance.** Never call `fetch` or create a new
  `axios.create()` — go through `src/api/client.ts` so JWT refresh and
  envelope unwrap run consistently.
- **Typed wrappers.** Use `get<T>`, `post<T>`, `put<T>`, `patch<T>`,
  `del<T>` from `src/api/client.ts` — they return `Promise<T>` (the
  unwrapped data), not an AxiosResponse.
- **UI primitives are owned in-house.** When you need a new Radix
  primitive, add it under `src/components/ui/` following the
  Avatar/DropdownMenu pattern (`forwardRef` + `cn(...)` + brand classes).
  Do NOT pull in an external shadcn/ui or third-party UI package —
  Syntera.React keeps its visual identity self-contained.
- **Form state.** Use uncontrolled forms (`FormData` + `defaultValue`)
  for create/edit drawers. State libraries like React Hook Form can be
  added later for complex forms; keep the surface simple for now.
- **TanStack Query.** Read endpoints → `useQuery`; mutations →
  `useMutation` + `queryClient.invalidateQueries(...)` on success.
  `retry`: no retry for 4xx, max 3 retries for 5xx / network.
  `staleTime`: 60s. `refetchOnWindowFocus: false`.
- **Branding.** Always reference `var(--color-primary)`,
  `var(--color-accent)`, `var(--primary)`, etc. — never raw hex. Brand
  palette lives in `src/index.css` (7 themes, light + dark each).
- **Accessibility.** Every button has an `aria-label` when its content
  is icon-only. Every form `<label>` wraps its input.
- **TypeScript.** `noUnusedLocals`, `noUnusedParameters`,
  `noFallthroughCasesInSwitch`, `erasableSyntaxOnly` are enabled. Path
  aliases (`@/`, `~`) are NOT configured — use relative imports.
