# Syntera.React

The React 19 single-page application for the **Syntera Pharmaceutical
Commerce Suite**. Built on Vite 8 + Tailwind v4 + TanStack Query v5,
and consuming the shared **`@sebastianbelmero/kalventis-ui`** component
library.

> See the parent [../README.md](../README.md) for full architecture,
> setup, and deployment docs. This file is a quick developer reference.

## Quick start

```bash
# From this directory:
bun install                       # also links local kalventis-ui
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
| `src/components/` | Reusable `DataTable`, `Modal`, `Field` primitives |
| `src/lib/` | `cn()` class composer + ID formatters |
| `src/pages/` | One folder per domain: auth, dashboard, catalog, parties, inventory, sales, settings |
| `src/routes/` | `RequireAuth`, `RequireRole` guards |
| `src/store/` | Zustand auth store (in-memory tokens) |
| `src/types/` | Mirror of backend DTOs (single file) |
| `src/index.css` | Tailwind v4 entry + Kalventis brand token overrides |

## Conventions

- **One Axios instance.** Never call `fetch` or create a new
  `axios.create()` — go through `src/api/client.ts` so JWT refresh
  and envelope unwrap run consistently.
- **Typed wrappers.** Use `get<T>`, `post<T>`, `put<T>`, `patch<T>`,
  `del<T>` from `src/api/client.ts` — they return `Promise<T>` (the
  unwrapped data), not an AxiosResponse.
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
