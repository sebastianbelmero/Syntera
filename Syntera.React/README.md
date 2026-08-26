# Syntera.React

The React 19 single-page application for the **Syntera Pharmaceutical
Commerce Suite**. Built on Vite 8 + Tailwind v4 + TanStack Query v5,
with a **fully self-contained UI layer** — all primitives (Avatar,
DropdownMenu, AdminLayout, AppSidebar, AppHeader, AppBreadcrumb),
the theme store, the token provider, and the brand design tokens
live in this repo's `src/`. No external component library is
required at runtime.

> See the parent [../README.md](../README.md) for full architecture,
> setup, and deployment docs. This file is a quick developer reference.

## Quick start

```bash
# From this directory:
bun install                       # installs only Syntera.React deps
bun run dev                       # http://localhost:5173 (proxies /api to 5113)
bun run build                     # production build → dist/
bun run typecheck                 # tsc --noEmit
bun run lint                      # oxlint
```

## Environment

Vite reads `VITE_API_BASE_URL` (default: empty, which makes the
Axios client use the relative `/api` prefix that Vite proxies to
`http://localhost:5113` in dev). For production, set
`VITE_API_BASE_URL=https://api.syntera.example.com`.

## Folder map

| Path | Purpose |
| --- | --- |
| `src/api/` | Single Axios client + per-aggregate endpoint helpers |
| `src/components/ui/` | In-house Radix-based primitives (Avatar, DropdownMenu, …) |
| `src/components/layout/` | Admin shell: AdminLayout, AppSidebar, AppHeader, AppBreadcrumb |
| `src/components/` | App-level composites (DataTable, Modal, Field) |
| `src/providers/` | Context providers (TokenProvider — decouples axios from auth store) |
| `src/lib/` | `cn()` class composer + ID formatters |
| `src/pages/` | One folder per domain: auth, dashboard, catalog, parties, inventory, sales, settings |
| `src/routes/` | `RequireAuth`, `RequireRole` guards |
| `src/store/` | Zustand stores: `authStore` (in-memory tokens) + `themeStore` (dark/light) |
| `src/types/` | Mirror of backend DTOs (single file) |
| `src/index.css` | Tailwind v4 entry + Syntera brand design tokens (single source of truth) |

## Conventions

- **One Axios instance.** Never call `fetch` or create a new
  `axios.create()` — go through `src/api/client.ts` so JWT refresh
  and envelope unwrap run consistently.
- **Typed wrappers.** Use `get<T>`, `post<T>`, `put<T>`, `patch<T>`,
  `del<T>` from `src/api/client.ts` — they return `Promise<T>` (the
  unwrapped data), not an AxiosResponse.
- **UI primitives are owned in-house.** When you need a new Radix
  primitive (Dialog, Tabs, Select, etc.), add it under
  `src/components/ui/` following the Avatar/DropdownMenu pattern
  (forwardRef + `cn(...)` + Syntera brand classes). Do NOT pull in
  an external shadcn/ui or third-party UI package — Syntera.React
  keeps its visual identity self-contained.
- **Form state.** Use uncontrolled forms (`FormData` +
  `defaultValue`) for create/edit modals. State libraries like React
  Hook Form can be added later for complex forms; keep the surface
  simple for now.
- **TanStack Query.** Read endpoints → `useQuery`; mutations →
  `useMutation` + `queryClient.invalidateQueries(...)` on success.
  The mutation hooks are intentionally inlined into pages for
  visibility; extract into `src/hooks/` once a page grows beyond
  ~400 lines.
- **Branding.** Always reference `var(--primary)`, `var(--accent)`,
  etc. — never raw hex. Brand palette lives in `src/index.css`.
- **Accessibility.** Every button has an `aria-label` when its
  content is icon-only. Every form `<label>` wraps its input. The
  `Modal` closes on Escape and on backdrop click.
