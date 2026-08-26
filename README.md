# 🌿 Syntera — Pharmaceutical Commerce Suite

> **Vital Science, Vital Commerce.**
>
> A full-stack pharmaceutical inventory + point-of-sale platform built on
> **.NET 10 + React 19 + SQL Server 2022**, themed around the Syntera /
> Kalbe / Dankos / Hexpharm / Fima / GOF brand family. The default
> Syntera palette (navy `#0B3D6F` + teal `#00A7B5`) mirrors the
> official Syntera logo; five additional brand palettes ship alongside.

The repository is a **single monorepo with two projects**:

| Project | Path | Stack | Purpose |
| ------- | ---- | ----- | ------- |
| `Syntera.Api` | `Syntera.Api/` | .NET 10 ASP.NET Core | REST API + EF Core 10 + Identity + JWT |
| `Syntera.React` | `Syntera.React/` | React 19 + Vite 8 + Tailwind v4 | SPA front-end with a self-contained UI layer |

Syntera.React owns its **entire UI stack** in-house — all Radix-based
primitives (`Avatar`, `DropdownMenu`, …), the admin shell
(`AdminLayout`, `AppSidebar`, `AppHeader`, `AppBreadcrumb`), the theme
store, the token provider, and the brand design tokens live under
`Syntera.React/src/`. No external component library is required at
runtime, so the project is free to evolve its visual identity without
being constrained by an upstream shared library.

---

## 📑 Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Domain Model](#domain-model)
3. [Quick start](#quick-start)
4. [Configuration & Secrets](#configuration--secrets)
5. [Database setup](#database-setup)
6. [Running the apps](#running-the-apps)
7. [Project layout](#project-layout)
8. [API surface](#api-surface)
9. [Front-end architecture](#front-end-architecture)
10. [Security model](#security-model)
11. [Brand identity](#brand-identity)
12. [Testing strategy](#testing-strategy)
13. [CI / CD](#ci--cd)
14. [Deployment](#deployment)
15. [Roadmap (v2 → v3)](#roadmap-v2--v3)
16. [Contributing](#contributing)
17. [License](#license)

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│                         Browser (SPA)                                │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │  React 19 + Vite 8 + Tailwind v4                              │  │
│  │  TanStack Query v5 (server cache)                             │  │
│  │  Axios (single client, auto-refresh, envelope unwrap)         │  │
│  │  Zustand (auth + theme, in-memory only)                       │  │
│  │  React Router v7 (nested routes, role guards)                  │  │
│  │  In-house UI: primitives + AdminLayout + theme store         │  │
│  └────────────────────────────┬───────────────────────────────────┘  │
└───────────────────────────────┼──────────────────────────────────────┘
                                │  HTTPS / JSON (Bearer JWT)
                                ▼
┌──────────────────────────────────────────────────────────────────────┐
│                    Syntera.Api (.NET 10)                              │
│                                                                      │
│  ┌─ Api layer ─────────────────────────────────────────────────────┐ │
│  │  Controllers → ApiControllerBase (uniform ApiResponse<T> env.)  │ │
│  │  GlobalExceptionMiddleware → 404/409/500 mapping                │ │
│  └────────────────────────────┬───────────────────────────────────┘ │
│                               ▼                                      │
│  ┌─ Application layer ────────────────────────────────────────────┐ │
│  │  Services (auth, catalog, inventory, sales, dashboard)         │ │
│  │  DTOs, FluentValidation, repository contracts                   │ │
│  └────────────────────────────┬───────────────────────────────────┘ │
│                               ▼                                      │
│  ┌─ Infrastructure layer ──────────────────────────────────────────┐│
│  │  AppDbContext (EF Core 10 + SQL Server)                          ││
│  │  Repository<T> + UnitOfWork (transactions, retries)              ││
│  │  Identity stores + JWT issuer                                    ││
│  │  DbSeeder (roles, admin, sample pharma data)                     ││
│  └────────────────────────────┬───────────────────────────────────┘│
│                               ▼                                      │
│  ┌─ Cross-cutting ─────────────────────────────────────────────────┐│
│  │  Serilog (console + file)  •  Swagger /health  •  Rate limiter   ││
│  └─────────────────────────────────────────────────────────────────┘│
└───────────────────────────────┼──────────────────────────────────────┘
                                ▼
                       ┌─────────────────┐
                       │  SQL Server 2022 │
                       │  (Docker / local)│
                       └─────────────────┘
```

### Layered responsibilities

- **Domain** (`Domain/`) — pure entities, enums, and domain exceptions.
  No EF Core, no HTTP, no DI. The single source of truth for business
  invariants ("an expired product cannot be sold", "stock is append-only").
- **Application** (`Application/`) — DTOs, FluentValidation rules,
  service interfaces, and service implementations. Coordinates
  repositories through `IUnitOfWork` so multi-step writes (e.g.
  creating a sale + decrementing stock) happen atomically.
- **Infrastructure** (`Infrastructure/`) — EF Core `AppDbContext`,
  generic repository base, per-aggregate repositories, ASP.NET
  Identity user store, and the idempotent `DbSeeder`.
- **Api** (`Api/`) — Controllers, the global exception middleware, and
  `Program.cs` bootstrap. Each controller is a thin adapter: validate,
  call service, map to `ApiResponse<T>`.

---

## Domain Model

The data model covers a typical apotek / pharmaceutical wholesale flow:

```
Category (1) ───────< (N) Product (N) >─────── (1) Supplier
                       │
                       │ (N)
                       ▼
              InventoryMovement (ledger)
                       ▲
                       │
Customer (1) ──< (N) Sale (1) ───────< (N) SaleItem (N) >─── (1) Product
```

- **Category** — self-referencing tree (Antibiotik › Penisilin, etc.)
- **Supplier** — distributor with BPOM licence number
- **Product** — obat with SKU, BPOM registration, potency, pack size,
  cost/selling price, batch number, expiry date, reorder level
- **InventoryMovement** — append-only ledger; **stock is never a
  column on Product**, it's `Σ Inbound − Σ Outbound`
- **Customer** — apotek / klinik / rumah sakit / B2B buyer
- **Sale** — invoice header with sub-total, tax, discount, grand total
- **SaleItem** — line with snapshotted unit price (historical accuracy)

### Drug classification (Indonesian Ministry of Health)

| Enum value | Local label | Icon circle |
| --- | --- | --- |
| `OverTheCounter` | Bebas | Hijau |
| `RestrictedOTC` | Bebas Terbatas | Biru |
| `PrescriptionOnly` | Keras | Merah (K) |
| `PharmacyOnly` | Wajib Apotek | — |
| `Narcotic` | Narkotika | BPOM special licence |

---

## Quick start

### Prerequisites

- **.NET 10 SDK** (`dotnet --version` → `10.0.x`)
- **Bun 1.3+** (or npm/pnpm if you prefer — `bun` scripts work with
  any Node 18+ runtime)
- **SQL Server 2022** (Docker or local install — any edition works,
  including Express/Developer)
- **Git** with HTTPS auth to GitHub

### 1 — Clone the monorepo

```bash
git clone https://github.com/sebastianbelmero/Syntera.git
# Resulting layout:
#   ~/code/Syntera        ← this repo
```

Syntera.React's UI is fully self-contained — no sibling repository
is required.

### 2 — Install React deps

```bash
cd Syntera/Syntera.React
bun install
```

### 3 — Restore .NET packages + create the database

```bash
cd ../Syntera.Api
dotnet restore
# Update the connection string in appsettings.Development.json
# (default: Server=localhost,1433;Database=Syntera;User Id=sa;Password=Passwordkuat123!)
dotnet ef database update
```

### 4 — Run both apps

```bash
# Terminal A — API at http://localhost:5113
cd Syntera.Api
dotnet run

# Terminal B — Vite dev server at http://localhost:5173
cd Syntera.React
bun run dev
```

Open `http://localhost:5173`, log in with the seeded admin account
(`admin@syntera.local` / `ChangeMe!Strong#1`), and explore the
dashboard, product catalog, inventory ledger, and POS flow.

> Swagger UI: `http://localhost:5113/docs`
> Health: `http://localhost:5113/health` and `/health/ready`

---

## Configuration & Secrets

The API reads configuration in this priority order (later wins):

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. **User Secrets** (Development only) — `dotnet user-secrets`
4. Environment variables prefixed with `SYNTERA_`
5. Command-line arguments

### Required secrets

Never commit real secrets. In Development, set them with User Secrets:

```bash
cd Syntera.Api

# 1. JWT signing key (≥ 32 chars, random)
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"

# 2. Initial admin password (change on first login)
dotnet user-secrets set "Seed:AdminPassword" "Strong#Password!2026"

# 3. Optional: override the DB connection string
dotnet user-secrets set "ConnectionStrings:Default" \
  "Server=localhost,1433;Database=Syntera;User Id=sa;Password=YourSaPassword;TrustServerCertificate=True"
```

In Production, set the same keys as environment variables:

```bash
export SYNTERA_JWT__SIGNINGKEY="$(openssl rand -base64 48)"
export SYNTERA_SEED__ADMINPASSWORD="Strong#Password!2026"
export SYNTERA_CONNECTIONSTRINGS__DEFAULT="Server=prod-sql;Database=Syntera;User Id=sa;Password=…;Encrypt=True"
```

### Connection string reference

The default in `appsettings.Development.json` is:

```
Server=localhost,1433;Database=Syntera;User Id=sa;Password=Passwordkuat!123;TrustServerCertificate=True;MultipleActiveResultSets=True
```

- `TrustServerCertificate=True` — Dev only. In production, install a
  real TLS cert on SQL Server and switch to `Encrypt=True` alone.
- `MultipleActiveResultSets=True` — required by EF Core for streaming
  query + side-effect writes in the same connection.

---

## Database setup

### Option A — SQL Server via Docker

```bash
docker run -d --name syntera-sql \
  -e "ACCEPT_EULA=Y" \
  -e "MSSQL_SA_PASSWORD=Passwordkuat!123" \
  -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest
```

> The SA password must match `Passwordkuat!123` (or whatever you set
> in `appsettings.Development.json`). SQL Server password policy
> requires ≥ 8 chars with upper, lower, digit, and symbol — the
> default satisfies this.

### Option B — Local SQL Server install

Use SQL Server Management Studio / Azure Data Studio to create a
blank database named `Syntera`. The first `dotnet ef database update`
will create all tables + the Identity schema.

### Create / update schema

```bash
cd Syntera.Api

# Apply existing migrations (also runs on app startup via DbSeeder)
dotnet ef database update

# Add a new migration after changing entities
dotnet ef migrations add YourChangeName --output-dir Migrations

# Drop & recreate (DEV ONLY)
dotnet ef database drop --force
dotnet ef database update
```

### Seeding

On every application start, `DbSeeder.SeedAsync` runs and:

1. Ensures the `Admin`, `Cashier`, `Pharmacist` roles exist.
2. Creates the initial admin user from `Seed:AdminEmail` /
   `Seed:AdminPassword` if it doesn't exist.
3. If `SEED_SAMPLE_DATA=true` (Development only), inserts a small
   demo dataset: 3 categories, 3 suppliers (PT Kalbe Farma,
   PT Hexpharm Jaya, PT Dankos Farma), 5 products (Amoxicillin,
   Paracetamol, Vitamin C, Ibuprofen, Cetirizine), 5 inbound stock
   movements, and 1 sample customer.

---

## Running the apps

| Command | What it does |
| --- | --- |
| `cd Syntera.Api && dotnet run` | Start API at `http://localhost:5113` |
| `cd Syntera.React && bun run dev` | Vite dev server at `http://localhost:5173` (proxies `/api/*` to the API) |
| `cd Syntera.React && bun run build` | Production build → `dist/` |
| `cd Syntera.React && bun run preview` | Preview the production build locally |
| `cd Syntera.Api && dotnet build` | Compile-check the API |
| `cd Syntera.React && bun run typecheck` | `tsc --noEmit` |

### Dev ports

| Service | Port | Notes |
| --- | --- | --- |
| Syntera.Api | 5113 | Configured in `Properties/launchSettings.json` |
| Syntera.React (dev) | 5173 | Proxies `/api/*` → `5113` via Vite |
| SQL Server | 1433 | Default SQL port |

---

## Project layout

```
Syntera/
├── Syntera.Api/                        # .NET 10 backend
│   ├── Domain/
│   │   ├── Entities/                   # BaseEntity, Product, Sale, ...
│   │   ├── Enums/                      # DrugClass, SaleStatus, ...
│   │   └── Exceptions/                 # DomainException, NotFoundException
│   ├── Application/
│   │   ├── Common/                    # ApiResponse<T>, PagedResult, PageQuery
│   │   ├── DTOs/                       # Auth, Catalog, Inventory, Sales, ...
│   │   ├── Interfaces/                 # IRepository<T>, IUnitOfWork, per-aggregate
│   │   ├── Services/                  # AuthService, ProductService, SaleService, ...
│   │   └── Validators/                # FluentValidation rules
│   ├── Infrastructure/
│   │   ├── Data/                      # AppDbContext, RepositoryBase, UnitOfWork
│   │   ├── Repositories/              # Concrete EF Core repositories
│   │   ├── Identity/                  # CurrentUserService
│   │   └── Seed/                      # DbSeeder (idempotent)
│   ├── Api/
│   │   ├── Controllers/               # Auth, Catalog, Parties, Sales, Dashboard
│   │   └── Middleware/                # GlobalExceptionMiddleware
│   ├── Extensions/                    # ServiceCollectionExtensions (DI wiring)
│   ├── Migrations/                    # EF Core migration history
│   ├── Program.cs                     # Bootstrap + seed
│   ├── appsettings.json               # Base config (no secrets)
│   └── appsettings.Development.json   # Dev config (with sample secrets)
│
├── Syntera.React/                      # React 19 + Vite SPA
│   ├── src/
│   │   ├── api/                       # Axios client + per-aggregate endpoints
│   │   ├── components/                # App-level composites (DataTable, Modal)
│   │   │   ├── ui/                    # In-house Radix primitives (Avatar, DropdownMenu, …)
│   │   │   └── layout/                # Admin shell (AdminLayout, AppSidebar, AppHeader, AppBreadcrumb)
│   │   ├── hooks/                     # (placeholder for future domain hooks)
│   │   ├── lib/                       # cn(), formatters
│   │   ├── pages/
│   │   │   ├── auth/                  # LoginPage (brand split-panel)
│   │   │   ├── dashboard/             # KPI cards + 14-day sales trend chart
│   │   │   ├── catalog/               # ProductsPage, CategoriesPage
│   │   │   ├── parties/               # SuppliersPage, CustomersPage
│   │   │   ├── inventory/             # InventoryPage (ledger)
│   │   │   ├── sales/                 # SalesPage (POS-like checkout)
│   │   │   └── settings/              # Profile + theme toggle + logout
│   │   ├── providers/                 # TokenProvider (decouples axios from auth store)
│   │   ├── routes/                    # RequireAuth, RequireRole guards
│   │   ├── store/                     # authStore (in-memory) + themeStore (dark/light)
│   │   ├── types/                     # API DTO mirror (single file)
│   │   ├── App.tsx                    # Router + AdminLayout shell
│   │   ├── main.tsx                   # Providers (Query, Token, Router, Toaster)
│   │   └── index.css                  # Brand palette + Tailwind v4 entry
│   ├── vite.config.ts                 # Vite + React Compiler + Tailwind v4
│   └── tsconfig.app.json              # Strict, verbatim, noUnusedLocals
│
└── README.md                           # ← you are here
```

---

## API surface

All endpoints return the **uniform `ApiResponse<T>` envelope**:

```json
{
  "success": true,
  "data": { /* endpoint-specific payload */ },
  "message": "optional success message"
}
```

On failure:

```json
{
  "success": false,
  "errorCode": "VALIDATION_FAILED",
  "message": "One or more fields failed validation.",
  "fieldErrors": [
    { "field": "name", "message": "Nama produk wajib diisi." }
  ]
}
```

### Endpoints

| Method | Path | Auth | Purpose |
| --- | --- | --- | --- |
| POST | `/api/auth/login` | anonymous | Email + password → JWT + refresh |
| POST | `/api/auth/refresh` | anonymous | Exchange refresh token for new access |
| GET | `/api/dashboard/summary` | authenticated | KPI counts + today/month/year totals |
| GET | `/api/dashboard/trend` | authenticated | 14-day trend + top-5 products |
| GET | `/api/categories` | authenticated | Paged category list |
| GET | `/api/categories/tree` | authenticated | Hierarchical category tree |
| POST | `/api/categories` | Admin | Create category |
| PUT | `/api/categories/{id}` | Admin | Update category |
| DELETE | `/api/categories/{id}` | Admin | Soft-delete category |
| GET | `/api/suppliers` | authenticated | Paged supplier list |
| POST/PUT/DELETE | `/api/suppliers/...` | Admin | Supplier CRUD |
| GET | `/api/products` | authenticated | Search products (filter by category, supplier, active) |
| GET | `/api/products/{id}` | authenticated | Product detail |
| POST | `/api/products` | Admin, Pharmacist | Create product |
| PUT | `/api/products/{id}` | Admin, Pharmacist | Update product |
| DELETE | `/api/products/{id}` | Admin | Soft-delete product |
| POST | `/api/products/{id}/stock` | Admin, Pharmacist | Adjust stock (auto-writes inventory ledger) |
| GET | `/api/inventory` | authenticated | Paged stock movements |
| GET | `/api/inventory/product/{id}` | authenticated | Movement history for one product |
| POST | `/api/inventory` | Admin, Pharmacist | Record inbound/outbound/adjustment |
| GET | `/api/customers` | authenticated | Paged customer list |
| POST/PUT/DELETE | `/api/customers/...` | Admin, Cashier | Customer CRUD |
| GET | `/api/sales` | authenticated | Paged invoice list |
| GET | `/api/sales/{id}` | authenticated | Invoice detail with items |
| POST | `/api/sales` | Admin, Cashier | Create sale (atomic with stock deduction) |
| PATCH | `/api/sales/{id}/status` | Admin, Cashier | Transition sale status (guarded) |
| GET | `/health` | anonymous | Liveness probe |
| GET | `/health/ready` | anonymous | Readiness probe (SQL Server ping) |
| GET | `/docs` (Dev only) | anonymous | Swagger UI |

---

## Front-end architecture

### Single Axios client

`src/api/client.ts` exports one configured axios instance. Every page
imports typed wrappers (`get<T>`, `post<T>`, `put<T>`, `patch<T>`,
`del<T>`). The instance:

1. Injects the Bearer token from the Zustand auth store on every
   request.
2. Unwraps the `ApiResponse<T>` envelope so call sites see the inner
   `data` directly.
3. Catches 401s, requests a refresh **once per concurrent burst**,
   replays the original request, and on failure redirects to `/login`.
4. Maps failures to a single `ApiError` type carrying `code`,
   `message`, `status`, and `fieldErrors` — so pages can react with
   a single `catch`.

### Routing & guards

React Router v7 with nested routes. `RequireAuth` redirects to
`/login` when unauthenticated. `RequireRole` shows a 403 page when
the user lacks any of the required roles.

### Data fetching

TanStack Query v5 is used for read endpoints (dashboard summary,
category/supplier dropdowns, etc.). Mutations invalidate the relevant
query keys (`products`, `inventory`, `dashboard-summary` …) so the UI
stays in sync without manual refetch.

### Reusable primitives

- `DataTable<T>` — generic paged table with search, pagination, and
  per-row action buttons. Used by every list page.
- `Modal` — accessible dialog with Escape-to-close, backdrop, and a
  consistent footer slot.
- `Field` + `inputClass` + `btnPrimary` / `btnGhost` — design tokens
  for form inputs and buttons; DRY across all forms.

### Brand styling

`src/index.css` is the **single source of truth** for the brand
design system. It defines all CSS variables (`--primary`, `--accent`,
`--background`, `--border`, radii, spacing, …), the dark-mode
overrides, and the Tailwind v4 `@theme inline` mapping that exposes
the variables as `bg-primary` / `text-muted-foreground` / etc.
utilities. Every component references these variables — never raw
hex — so a future rebrand touches one file.

---

## Security model

### Authentication

- ASP.NET Core Identity with email-as-username, strong password
  policy (≥ 8 chars, upper + lower + digit + symbol), and lockout
  after 5 failed attempts for 15 minutes.
- JWT bearer tokens (HS256). Access token 15 min in prod, 60 min in
  dev. Refresh token 7 days prod, 30 days dev.
- Refresh tokens are signed with the **same** key as access tokens
  but a separate audience (`Syntera.Web-refresh`) so a stolen access
  JWT can't be replayed as a refresh grant.

### Authorization

Three roles ship out of the box:

| Role | Capabilities |
| --- | --- |
| `Admin` | Full CRUD on every aggregate + manage users |
| `Pharmacist` | Create / edit products, adjust stock, record inventory movements |
| `Cashier` | Create / update sales, manage customers, view everything |

Role checks are enforced via `[Authorize(Roles = "...")]` on
controllers and mirrored in the React router (`RequireRole`).

### Defence in depth

- **CORS** — locked to the configured origins (defaults to
  `localhost:5173` and `4173`).
- **Rate limiting** — 500 req/min global default, 60 req/min on the
  `strict` policy (tag sensitive endpoints with
  `[EnableRateLimiting("strict")]`).
- **Global exception middleware** — every unhandled exception is
  logged with Serilog and returned as a uniform JSON envelope. In
  production, internal error text is hidden from the client.
- **Soft-delete** — `IsDeleted` flag + EF Core query filters means
  deleted records stay for audit but never leak into the UI.
- **Audit fields** — `CreatedAt` and `UpdatedAt` are stamped by
  `AppDbContext.SaveChangesAsync`, never by application code.
- **Stock ledger** — on-hand quantity is computed as
  `Σ Inbound − Σ Outbound` from the `InventoryMovements` table;
  there is no mutable `Stock` column on `Product`, so concurrent
  writes can't overwrite each other.
- **Append-only inventory** — the ledger is never edited; corrections
  are written as new `Adjustment` rows with a note.

### What's intentionally NOT done

- **No localStorage for tokens.** The Zustand auth store lives in
  memory only — users re-login on refresh, but XSS-based token theft
  is impossible.
- **No hardcoded secrets in appsettings.** The dev file ships a
  placeholder; real secrets go to User Secrets (dev) or env vars
  (prod).
- **No `TrustServerCertificate` in prod.** Dev-only shortcut; prod
  must use a real cert and `Encrypt=True`.

---

## Brand identity

Built around six brand palettes: **Syntera** (canonical navy + teal,
derived from the official Syntera logo), **Kalbe Farma** (crimson),
**Dankos Farma** (royal blue), **Hexpharm Jaya** (teal), **Fima
Internasional** (violet), and **Global Onkolab Farma / GOF** (amber).
Each palette ships both light and dark variants; users switch live
from the header dropdown or the Settings page. The default Syntera
palette is the only one tied to the actual product logo.

| Token | Hex | Usage |
| --- | --- | --- |
| `--primary` | `#0B3D6F` | Buttons, links, brand accents (Syntera navy) |
| `--primary-hover` | `#082A52` | Pressed/hover state |
| `--accent` | `#00A7B5` | Highlights, KPI tiles (Syntera teal) |
| `--accent-foreground` | `#042A30` | Text on accent background |
| `--background` | `#F5F8FB` (light) / `#0A1428` (dark) | Page background |
| `--surface` | `#EAF1F8` (light) / `#11243F` (dark) | Cards, sub-surfaces |
| `--border` | `#D0DDE9` (light) / `#1F3A5C` (dark) | Lines, dividers |

The "Vital Science" theme pairs the navy + teal palette with the modern
humanist sans-serif (Inter / system-ui). Every component references
these tokens, so a future rebrand touches one file.

---

## Testing strategy

The current shipping tests focus on build correctness and manual
verification. The recommended next steps for a real QA pipeline:

### Unit tests

- **Application layer** — service classes (`ProductService`,
  `SaleService`, `AuthService`) tested against an in-memory
  `IUnitOfWork` + fake repositories. No DB, no HTTP.
- **Domain** — pure entity behavior (e.g. `SaleService` rejects
  expired products, negative stock, illegal status transitions).

### Integration tests

- `WebApplicationFactory<Program>` against an in-memory or
  TestContainers SQL Server. Hit the API end-to-end:
  - `/api/auth/login` → 200 with valid creds, 401 with bad
  - `/api/products/{id}/stock` → stock decremented, ledger row added
  - `/api/sales` → 201 with valid cart; 409 with expired product;
    409 with insufficient stock

### E2E tests

- Playwright against the React SPA:
  - Login flow (admin + cashier + pharmacist)
  - Create product → adjust stock → make sale → verify dashboard
  - Role guard: cashier blocked from /products create button

### Performance

- BenchmarkDotNet for hot paths (`ProductRepository.GetStockAsync`,
  `SaleRepository.NextInvoiceNumberAsync`).
- k6 load script for `/api/dashboard/*` endpoints (cache candidate).

---

## CI / CD

A minimal GitHub Actions workflow is recommended at
`.github/workflows/ci.yml`:

```yaml
name: CI
on:
  push: { branches: [main] }
  pull_request: { branches: [main] }

jobs:
  api:
    runs-on: ubuntu-latest
    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          ACCEPT_EULA: Y
          MSSQL_SA_PASSWORD: Passwordkuat!123
        ports: ["1433:1433"]
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet restore Syntera.Api
      - run: dotnet build Syntera.Api --no-restore
      - run: dotnet test Syntera.Api --no-build --logger trx
      - run: dotnet ef database update --project Syntera.Api --no-build
        env:
          ConnectionStrings__Default: Server=localhost,1433;Database=Syntera;User Id=sa;Password=Passwordkuat123!;TrustServerCertificate=True

  web:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { submodules: recursive }
      - uses: oven-sh/setup-bun@v2
      - run: bun install --cwd Syntera.React
      - run: bun run build --cwd Syntera.React
      - run: bun run typecheck --cwd Syntera.React
```

> Syntera.React's UI is fully self-contained, so no sibling build
> step is required.

---

## Deployment

### Recommended target topology

```
                   Internet
                      │
              ┌───────┴────────┐
              │  Reverse proxy  │   (nginx / Caddy / Azure Front Door)
              │  TLS + WAF      │
              └───────┬────────┘
                      │
        ┌─────────────┴─────────────┐
        ▼                            ▼
  ┌─────────────┐            ┌─────────────┐
  │ Syntera.Api │            │ Syntera.Api │   ← 2+ instances behind LB
  │  container  │            │  container  │     (stateless, scale-out)
  └──────┬──────┘            └──────┬──────┘
         └──────────┬───────────────┘
                    ▼
          ┌──────────────────┐
          │  SQL Server 2022  │   (managed / Azure SQL / AWS RDS)
          │  Always-On AG     │
          └──────────────────┘
```

### Docker

A `Dockerfile` for the API (recommended next step):

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5113

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Syntera.Api/ ./Syntera.Api/
RUN dotnet restore Syntera.Api/Syntera.Api.csproj
RUN dotnet publish Syntera.Api/Syntera.Api.csproj -c Release -o /app/publish

FROM base
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:5113
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "Syntera.Api.dll"]
```

The React app builds to a static `dist/` folder; serve it via any CDN
or static host (Vercel, Cloudflare Pages, nginx).

### Production checklist

- [ ] Rotate `Jwt:SigningKey` (≥ 48 random bytes) and store in a real
      secret manager (Azure Key Vault, AWS Secrets Manager, Doppler).
- [ ] Set `Seed:AdminPassword` once, change it from the UI on first
      login, then remove the env var.
- [ ] Use real SQL Server TLS cert (`Encrypt=True`,
      `TrustServerCertificate=False`).
- [ ] Set `Cors:Origins` to the production front-end domain only.
- [ ] Configure Serilog to ship to a real sink (Seq, Datadog, Loki).
- [ ] Enable Prometheus metrics exporter + OpenTelemetry traces.
- [ ] Run smoke tests against `/health/ready` after every deploy.
- [ ] Set up alerting on `429` rate-limit bursts (auth endpoint
      specifically).

---

## Roadmap (v2 → v3)

The v1 release delivers a working catalog + inventory + POS flow.
The next iterations aim at a production-grade, multi-tenant pharma
commerce platform:

### v2 — Operational scale

- **Multi-tenancy** — branch / apotek concept, per-tenant inventory
  and pricing, role scoping by tenant.
- **Prescription upload** — pharmacist uploads PDF / image of a
  resep; OCR fills the cart; the original is stored for audit.
- **BPOM batch recall** — flag a batch as recalled → cascade-block
  sale of every product with that batch number across all tenants.
- **Real-time stock** — SignalR hub pushes inventory updates to all
  connected dashboards so concurrent cashiers see the latest stock
  without polling.
- **Audit log** — append-only `AuditLog` table capturing every
  mutation (actor, action, before/after diff) for compliance audits.
- **Distributed cache** — Redis for dashboard summary + token
  blacklist (for forced logout / token revocation).
- **Reporting** — `/api/reports/sales?from=&to=` exporting XLSX
  via ClosedXML, scheduled for monthly email.
- **OpenTelemetry** — traces from API → EF Core → SQL Server, all
  exported to Tempo/Jaeger.

### v3 — Commerce growth

- **E-commerce** — public storefront for B2C customers, integrated
  with Midtrans / Xendit for payment, JNE / Sicepat for shipping.
- **Loyalty** — point accrual per sale, redeemable as discounts.
- **WhatsApp delivery** — Twilio integration for invoice delivery
  + status notifications.
- **Accounting export** — journal entry export to Accurate / Zahir
  via CSV or their respective REST APIs.
- **Mobile companion** — React Native app for pharmacists to scan
  barcodes and record outbound movements from the floor.
- **AI** — demand forecasting per product per week based on the
  `Sales` history, surfaced on the dashboard reorder card.

### Architecture drift to watch

- **Read model split** — once the dashboard becomes a hot path,
  introduce a separate `IDashboardReadModel` backed by Dapper or a
  materialised view, so EF Core stays focused on transactional
  writes.
- **CQRS** — if command volume grows beyond ~50 writes/sec, split
  commands and queries into separate projects and consider MediatR
  or a custom pipeline.
- **Event sourcing** — for high-compliance scenarios (narcotics),
  consider an event-sourced `InventoryMovement` aggregate with
  snapshots.
- **gRPC** — for internal service-to-service calls (e.g. a future
  billing service), prefer gRPC over JSON to cut payload size.

---

## Contributing

1. Branch from `main`: `git checkout -b feat/your-feature`.
2. Write code; run `dotnet build` and `bun run typecheck` locally.
3. Add tests for any new domain invariant or controller route.
4. Commit with conventional commits
   (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`).
5. Open a PR — CI runs build + tests; the maintainer reviews.
6. Squash-merge to `main`; the deploy pipeline ships.

### Code style

- **C#** — file-scoped namespaces, nullable reference types on,
  `private readonly` fields with `_` prefix, no regions, async
  all-the-way-down (no `.Result` / `.Wait()`).
- **TypeScript** — strict mode, `verbatimModuleSyntax` on,
  `noUnusedLocals` on. Use `import type` for types. Function
  components only. Hooks prefixed with `use`.
- **Tailwind v4** — prefer `var(--token)` references over arbitrary
  hex; reuse `btnPrimary` / `inputClass` helpers in forms.

---

## License

Copyright © 2026 Syntera. All rights reserved.

This repository is private and intended for internal use by the
Kalbe Farma affiliated pharmaceutical commerce team. Contact the
maintainer for licensing / contribution inquiries.
