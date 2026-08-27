# 🌿 Syntera IAM

> **One Platform. One Standard. One Direction.**
>
> A multi-tenant **Identity & Access Management** platform for the Syntera /
> Kalbe / Dankos / Hexpharm / Fima / GOF / Kalventis pharmaceutical group.
> Built on **.NET 10 + React 19 + SQL Server 2022**, themed around each
> site's brand identity.

This repository was refactored from a pharmaceutical inventory + POS
application into a centralized **IAM platform** that authenticates users
from 6+ affiliated sites via their respective LDAP directories, manages
role-based access control with delegated administration, and provides a
tamper-evident audit trail for compliance (CFR Part 11 / GxP).

---

## 📑 Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Login Flow with LDAP Domain Routing](#login-flow-with-ldap-domain-routing)
3. [Permission Model (Hybrid RBAC + Direct Permission)](#permission-model-hybrid-rbac--direct-permission)
4. [Multi-Tenant Database Architecture](#multi-tenant-database-architecture)
5. [3-Tier Admin Delegation](#3-tier-admin-delegation)
6. [Quick Start](#quick-start)
7. [Configuration & Secrets](#configuration--secrets)
8. [Database Setup](#database-setup)
9. [Running the Apps](#running-the-apps)
10. [Project Layout](#project-layout)
11. [API Surface](#api-surface)
12. [Front-End Architecture](#front-end-architecture)
13. [Security Model](#security-model)
14. [Audit & Compliance](#audit--compliance)
15. [Brand Theming](#brand-theming)
16. [Operational Runbook](#operational-runbook)

---

## Architecture Overview

Syntera is a **single monorepo with two projects**:

| Project | Path | Stack | Purpose |
| ------- | ---- | ----- | ------- |
| `Syntera.Api` | `Syntera.Api/` | .NET 10 ASP.NET Core | REST API + EF Core 10 + JWT + LDAP |
| `Syntera.React` | `Syntera.React/` | React 19 + Vite 8 + Tailwind v4 | SPA front-end for admin UI |

**Key design principles:**

1. **Multi-tenant by database isolation** — One platform database (`syntera_master`)
   + one isolated database per site (`syntera_kalventis`, `syntera_kalbe`, ...).
   A compromise of Site A's database never exposes Site B's data.

2. **Email-domain routing** — Users log in with a single form; the platform
   routes authentication to the correct LDAP server based on the email's
   domain (`@kalventis.com` → LDAP Kalventis, `@kalbe.co.id` → LDAP Kalbe, ...).

3. **3-tier delegated administration** — Platform Admin (`admin@syntera.com`)
   manages sites & role templates. Site Business Admins (delegated per site)
   provision users and assign roles. End Users access features per their
   effective permission set.

4. **Hybrid RBAC + Direct Permission** — Users receive permissions from
   assigned roles PLUS direct grants (with mandatory expiry, reason, and
   approver) for temporary elevated access. Direct permissions auto-revoke
   at expiry (max 90 days).

5. **Tamper-evident audit log** — Append-only, hash-chained entries.
   UPDATE/DELETE rejected at the EF Core pipeline level. Retention
   configurable (default 10 years) for CFR Part 11 compliance.

6. **DB-stored brand themes** — Each site's palette (light + dark) is
   stored as JSON in the platform DB and cached in-memory on the API.
   Platform Admin can update brand colors without redeploying.

### Architecture Diagrams

Four architecture diagrams are generated and stored in `docs/diagrams/`:

| Diagram | File | Description |
| ------- | ---- | ----------- |
| Login Flow | `01_login_flow.png` | Email domain → LDAP routing → JWT issuance |
| Permission Model | `02_permission_model.png` | Hybrid RBAC + Direct Permission with expiry |
| Multi-Tenant DB | `03_multi_tenant_db.png` | Platform DB + per-site DB isolation |
| Role Hierarchy | `04_role_hierarchy.png` | 3-tier admin delegation with permission scope |

---

## Login Flow with LDAP Domain Routing

```
User input email + password
        ↓
┌─────────────────────────────────────────────────────┐
│ admin@syntera.com        → Platform Admin (local)   │
│ xxx@kalventis.com        → Auth via LDAP Kalventis  │
│ xxx@kalbe.co.id          → Auth via LDAP Kalbe       │
│ xxx@dankos.com           → Auth via LDAP Dankos      │
│ xxx@hexpharm.com         → Auth via LDAP Hexpharm    │
│ xxx@fima.com             → Auth via LDAP Fima        │
│ xxx@gof.com              → Auth via LDAP GOF         │
└─────────────────────────────────────────────────────┘
        ↓
LDAP bind (always LDAPS or StartTLS — never plain)
        ↓
Pre-provisioning check (user must exist in site DB)
        ↓
Issue JWT (15 min) + Refresh Token (24h, rotating)
Apply site theme (light/dark from user preference)
Write audit log (immutable, hash-chained)
        ↓
✓ Authenticated → redirect to /dashboard
```

**Key points:**

- **No fallback** — If LDAP is down, login fails. Platform Admin (`@syntera.com`)
  is the only user that can log in without LDAP.
- **Pre-provisioning required** — Even after LDAP authentication succeeds,
  the user must exist in the site database (provisioned by the Site Business
  Admin) before they can access the platform. This prevents unauthorized
  users from any LDAP from logging in.
- **LDAP injection protection** — User email is escaped per RFC 4515 before
  being inserted into the LDAP filter (`* ( ) \ NUL` and bytes < 0x20).
- **Account lockout** — After 5 failed attempts per IP+email, the account
  is locked for 15 minutes (configurable via platform settings).

---

## Permission Model (Hybrid RBAC + Direct Permission)

```
effective = role_permissions(user) ∪ direct_permissions(user, not_expired)
denied    = explicit_deny_grants(user)
final     = effective \ denied
```

### Permission Sources

1. **RBAC path (stable, audit-friendly):**
   `User → UserRole → Role → RolePermission → Permission`

2. **Direct permission path (temporary, with mandatory expiry):**
   `User → UserPermission → Permission`
   Every direct grant MUST have:
   - `Reason` (required text, min 10 chars)
   - `ApprovedBy` (FK → User, the Site Business Admin)
   - `ExpiresAt` (required, max 90 days from grant)
   - Auto-revoked by a background job at expiry

### Permission Granularity

Fine-grained, namespace-scoped keys: `resource.action[.scope]`

| Group | Example permissions |
| ----- | ------------------- |
| Site Management | `site.create`, `site.read`, `site.update`, `site.disable` |
| LDAP Configuration | `ldap.read`, `ldap.write`, `ldap.test_connection` |
| Theme Management | `theme.read`, `theme.write` |
| Role Templates | `role_template.read`, `role_template.write`, `role_template.publish` |
| Delegation | `business_admin.assign`, `business_admin.revoke` |
| Platform Audit | `platform.audit.read`, `platform.config.read/write` |
| Platform Users | `platform_user.read/create/update/disable` |
| User Management (Site) | `user.read`, `user.write`, `user.disable`, `user.sync` |
| Role Assignment (Site) | `role.read`, `user_role.assign`, `user_role.revoke` |
| Permission Grants (Site) | `permission.read`, `permission.grant`, `permission.revoke` |
| Site Audit | `audit.read`, `report.read` |

### Permission Cache

- In-memory cache per user with 5-min TTL.
- Cache invalidation: `User.PermissionsVersion` is bumped on any role/permission
  change. The JWT carries this version; if it doesn't match the current value
  on a request, the permission engine re-resolves the effective set.

### Authorization Attributes (Backend)

```csharp
[HasPermission("user.write")]      // Requires the specific permission in JWT
[PlatformAdminOnly]                 // admin@syntera.com only
[SiteBusinessAdmin]                 // Site admin or platform admin
```

---

## Multi-Tenant Database Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Application Tier — single .NET 10 API process                   │
│                                                                  │
│  ISiteDbContextFactory ──── resolves correct DbContext           │
│         ↓ based on JWT site_id claim                             │
│  PlatformDbContext (singleton) ── always points to master        │
│  SiteDbContext (scoped per request) ── per-site connection       │
└─────────────────────────────────────────────────────────────────┘
        ↓
┌─────────────────────────────────────────────────────────────────┐
│ Database Tier — 7 databases total                                │
│                                                                  │
│  syntera_master (PLATFORM)                                       │
│    ├── Sites, SiteLdapDomains, SiteLdapConfigs, SiteThemes       │
│    ├── RoleTemplates, RoleTemplatePermissions                    │
│    ├── PlatformUsers, RefreshTokens (platform scope)             │
│    ├── PlatformSettings (audit retention, token lifetimes)       │
│    └── AuditLogs (platform-level)                                │
│                                                                  │
│  syntera_kalventis   syntera_kalbe   syntera_dankos              │
│  syntera_hexpharm    syntera_fima    syntera_gof                 │
│    Each contains:                                                │
│    ├── Users, Roles, Permissions, UserRoles, RolePermissions     │
│    ├── UserPermissions (direct grants)                           │
│    ├── RefreshTokens (site scope)                                │
│    ├── AuditLogs (site-level)                                    │
│    └── UserSyncHistory                                           │
└─────────────────────────────────────────────────────────────────┘
```

### Isolation Guarantees

- **Connection-level isolation** — Each Site DbContext uses its own
  connection string; no shared connection pool.
- **No cross-site JOIN** — Forbidden at the repository layer. Cross-site
  aggregation done in application code.
- **Backup / Restore per-site** — Each DB backed up independently; site
  outage does not affect others.
- **Per-site encryption keys** — TDE per DB with distinct certificate;
  key rotation isolated per site.

---

## 3-Tier Admin Delegation

### Tier 1 — Platform Admin (`admin@syntera.com`)

- Local bcrypt credential (must work even if all site LDAPs are down)
- Stored in `syntera_master.PlatformUsers`
- Permissions: `site.*`, `ldap.*`, `theme.*`, `role_template.*`,
  `business_admin.assign/revoke`, `platform.*`
- **Cannot:** read site business data, view individual users, assign
  roles to end users (only delegate to business admins)

### Tier 2 — Site Business Admin (per site)

- Authenticated via site LDAP, scoped to own site DB
- Permissions: `user.*`, `role.read`, `user_role.assign/revoke`,
  `permission.grant/revoke`, `user.sync`, `audit.read`
- **Cannot:** create role templates, modify LDAP config, view other
  sites, escalate to Platform Admin

### Tier 3 — End User (per site)

- Authenticated via site LDAP
- Permissions come from assigned roles + direct grants
- Standard roles: `viewer` (read-only), `site-business-admin`
- Direct permission override possible with expiry

---

## Quick Start

### Prerequisites

- .NET 10 SDK
- Node.js 24+ (or Bun)
- SQL Server 2022 (local or remote)

### Backend

```bash
cd Syntera.Api

# Set required secrets (Development only — use env vars in production)
dotnet user-secrets set "Jwt:SigningKey" "your-32-char-min-secret-key-here-xxxxxxx"
dotnet user-secrets set "Seed:PlatformAdminPassword" "YourStrongPa55!"

# Set connection string (Development)
dotnet user-secrets set "ConnectionStrings:Platform" "Server=localhost,1433;Database=syntera_master;User Id=sa;Password=YourSaPassword;TrustServerCertificate=True;MultipleActiveResultSets=True"

# Run
dotnet run
```

The API will be available at `http://localhost:5000` (or as configured in `launchSettings.json`).
Swagger UI at `http://localhost:5000/docs`.

### Frontend

```bash
cd Syntera.React
bun install   # or npm install
bun run dev   # or npm run dev
```

The SPA will be available at `http://localhost:5173`.

---

## Configuration & Secrets

### Backend (`appsettings.json`)

| Key | Description | Required in Production |
| --- | ----------- | ---------------------- |
| `ConnectionStrings:Platform` | SQL Server connection string for `syntera_master` | ✅ Yes |
| `Jwt:SigningKey` | HS256 signing key, min 32 chars | ✅ Yes (env var `SYNTERA_Jwt__SigningKey`) |
| `Jwt:AccessTokenMinutes` | JWT lifetime (default 15) | Optional |
| `Jwt:RefreshTokenDays` | Refresh token lifetime (default 1) | Optional |
| `Cors:AllowedOrigins` | Comma-separated list of allowed origins | ✅ Yes (fail-closed if empty in Production) |
| `Cors:DevOrigins` | Development-only origins (localhost) | Optional |
| `DataProtection:KeyPath` | Directory for DPAPI key ring (LDAP credential encryption) | Optional (default `/var/lib/syntera/keys`) |
| `Seed:PlatformAdminEmail` | Platform admin email (default `admin@syntera.com`) | Optional |
| `Seed:PlatformAdminPassword` | Initial platform admin password | ✅ Yes (env var) |
| `Audit:RetentionYears` | Audit log retention (default 10) | Optional |

### Fail-Fast Startup Checks (Production)

The API refuses to start in Production if any of these conditions are true:

1. `Jwt:SigningKey` missing or shorter than 32 chars
2. `Cors:AllowedOrigins` missing or empty
3. `Ldap:AllowPlain=true` (plain LDAP forbidden in production)

### Environment Variable Convention

All config keys can be overridden via environment variables using `__` (double underscore) as the hierarchy separator:

```bash
SYNTERA_ConnectionStrings__Platform="Server=..."
SYNTERA_Jwt__SigningKey="..."
SYNTERA_Seed__PlatformAdminPassword="..."
```

---

## Database Setup

### Initial Setup (Platform DB)

In Development, the API auto-creates the platform database on startup via
`EnsureCreatedAsync()`. For Production, use EF Core migrations:

```bash
cd Syntera.Api
dotnet ef migrations add InitialPlatform --context PlatformDbContext --output-dir Migrations/Platform
dotnet ef database update --context PlatformDbContext
```

### Site Database Setup

Each site database must be created manually (or via your DB provisioning
pipeline) with the connection string stored in the platform DB's `Sites`
table. The schema is identical across all site DBs.

To provision a new site database:

1. Platform Admin creates the site via UI (`POST /api/platform/sites`)
2. Create the database manually in SQL Server with the same name as configured
3. The schema is created on first site-DbContext access via `EnsureCreatedAsync()`
   (Development only — use migrations in Production)

### Seeding Default Data

The seeder (`DbSeeder.SeedPlatformAsync`) creates:

- Default platform settings (audit retention = 10 years, token lifetimes, etc.)
- Default role templates:
  - `viewer` — read-only access
  - `site-business-admin` — manages users in own site

The Platform Admin user (`admin@syntera.com`) is **NOT** auto-seeded in the
current version. To create it manually:

```sql
USE syntera_master;
INSERT INTO PlatformUsers (Id, Email, PasswordHash, DisplayName, IsEnabled, CreatedAt, UpdatedAt)
VALUES (NEWID(), 'admin@syntera.com', '<bcrypt-hash>', 'Platform Admin', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
```

Generate the bcrypt hash via your favorite tool (e.g., `htpasswd -bnBC 12 "" "password" | tr -d ':\n'`).

---

## Running the Apps

### Development

```bash
# Terminal 1: Backend
cd Syntera.Api
dotnet run

# Terminal 2: Frontend
cd Syntera.React
bun run dev
```

### Production

```bash
# Backend
cd Syntera.Api
dotnet publish -c Release -o ./publish
./publish/Syntera.Api

# Frontend
cd Syntera.React
bun run build
# Serve dist/ via nginx, Apache, or any static file server
```

---

## Project Layout

```
Syntera/
├── README.md
├── docs/
│   └── diagrams/
│       ├── 01_login_flow.png
│       ├── 02_permission_model.png
│       ├── 03_multi_tenant_db.png
│       └── 04_role_hierarchy.png
│
├── Syntera.Api/
│   ├── Api/
│   │   ├── Controllers/
│   │   │   ├── Auth/AuthController.cs        # login, refresh, logout, profile
│   │   │   ├── Platform/
│   │   │   │   ├── SitesController.cs        # site CRUD + LDAP config + theme
│   │   │   │   └── RoleTemplatesController.cs
│   │   │   ├── Site/UsersController.cs       # user CRUD + role/perm assignment + sync
│   │   │   ├── AuditLogsController.cs
│   │   │   └── ApiControllerBase.cs
│   │   ├── Middleware/GlobalExceptionMiddleware.cs
│   │   └── ModelBinding/DataSourceLoadOptions.cs
│   │
│   ├── Application/
│   │   ├── DTOs/                              # request/response DTOs
│   │   │   ├── Auth/, Sites/, Users/, Roles/, Audit/, Common/
│   │   ├── Services/
│   │   │   ├── AuthService.cs                 # login orchestration
│   │   │   ├── JwtTokenService.cs             # JWT issuance + bcrypt hasher
│   │   │   ├── PermissionService.cs           # RBAC + direct perm resolution
│   │   │   ├── AuditService.cs                # hash-chained audit log writer
│   │   │   ├── ThemeService.cs                # DB-stored palettes + cache
│   │   │   ├── SiteManagementService.cs       # site/LDAP/theme CRUD
│   │   │   ├── UserManagementService.cs       # user CRUD + sync + grants
│   │   │   └── RoleTemplateService.cs         # template publish → clone
│   │   ├── Interfaces/Services/
│   │   └── Common/ApiResponse.cs
│   │
│   ├── Domain/
│   │   ├── Entities/
│   │   │   ├── BaseEntity.cs                  # audit + soft-delete base
│   │   │   ├── Site.cs                        # Site, SiteLdapDomain, SiteLdapConfig, SiteTheme
│   │   │   ├── User.cs                        # User, Role, Permission, UserRole, RolePermission, UserPermission
│   │   │   ├── PlatformEntities.cs            # RoleTemplate, PlatformUser, RefreshToken
│   │   │   └── AuditLog.cs                    # AuditLog, UserSyncHistory
│   │   └── Exceptions/DomainException.cs
│   │
│   ├── Infrastructure/
│   │   ├── Data/
│   │   │   ├── PlatformDbContext.cs           # master DB context
│   │   │   ├── SiteDbContext.cs               # per-site DB context
│   │   │   └── SiteDbContextFactory.cs        # resolves site DB by JWT claim
│   │   ├── Identity/CurrentUserService.cs     # JWT claim reader
│   │   ├── Authorization/
│   │   │   └── HasPermissionAttribute.cs      # [HasPermission], [PlatformAdminOnly], [SiteBusinessAdmin]
│   │   ├── Ldap/NovellLdapClient.cs           # LDAPS / StartTLS, search, bind, sync
│   │   ├── Security/LdapConfigProtector.cs    # DPAPI encryption for bind passwords
│   │   └── Seed/DbSeeder.cs
│   │
│   ├── Extensions/ServiceCollectionExtensions.cs
│   ├── Program.cs
│   ├── appsettings.json
│   └── appsettings.Development.json
│
└── Syntera.React/
    ├── src/
    │   ├── api/
    │   │   ├── client.ts                      # axios instance + 401 refresh
    │   │   ├── auth.ts                        # login, refresh, logout
    │   │   ├── platform.ts                    # sites, role templates
    │   │   ├── site.ts                        # users, roles, permissions, sync
    │   │   └── audit.ts                       # audit log query
    │   ├── store/
    │   │   ├── authStore.ts                   # tokens, profile, theme bundle
    │   │   └── themeStore.ts                  # light/dark mode preference
    │   ├── pages/
    │   │   ├── auth/LoginPage.tsx
    │   │   ├── dashboard/DashboardPage.tsx
    │   │   ├── platform/
    │   │   │   ├── SitesPage.tsx              # site CRUD + LDAP config + test
    │   │   │   └── RoleTemplatesPage.tsx      # template CRUD + publish
    │   │   ├── site/UsersPage.tsx             # user CRUD + role/perm grants + sync
    │   │   ├── audit/AuditLogsPage.tsx
    │   │   └── settings/SettingsPage.tsx
    │   ├── routes/guards.tsx                  # RequireAuth, RequirePlatformAdmin, RequireSiteAdmin
    │   ├── components/
    │   │   ├── layout/                        # AdminLayout, AppSidebar, AppHeader, AppBreadcrumb
    │   │   ├── ui/                            # Button, Badge, Avatar, Select, etc. (Radix-based)
    │   │   └── grid/                          # AppGrid + helpers (DevExtreme-based)
    │   ├── App.tsx
    │   └── main.tsx
    ├── package.json
    ├── vite.config.ts
    └── tsconfig*.json
```

---

## API Surface

All endpoints are JSON over HTTPS, returning a uniform `ApiResponse<T>` envelope:

```json
{
  "success": true,
  "data": { /* ... */ },
  "message": "Optional message"
}
```

On error:

```json
{
  "success": false,
  "errorCode": "BUSINESS_RULE_VIOLATION",
  "message": "Description of the error"
}
```

### Auth (anonymous)

| Method | Path | Description |
| ------ | ---- | ----------- |
| POST | `/api/auth/login` | Login by email + password (routes by domain) |
| POST | `/api/auth/refresh` | Refresh platform admin token |
| POST | `/api/auth/refresh-site` | Refresh site user token (with `siteId`) |

### Auth (authenticated)

| Method | Path | Description |
| ------ | ---- | ----------- |
| POST | `/api/auth/logout` | Revoke refresh token |
| GET | `/api/auth/profile` | Get current user profile from JWT |

### Platform Admin (`[PlatformAdminOnly]`)

| Method | Path | Description |
| ------ | ---- | ----------- |
| GET | `/api/platform/sites` | List all sites |
| POST | `/api/platform/sites` | Create a site |
| GET | `/api/platform/sites/{id}` | Get a site |
| PUT | `/api/platform/sites/{id}` | Update a site |
| POST | `/api/platform/sites/{id}/disable` | Disable a site |
| GET | `/api/platform/sites/{siteId}/ldap-config` | Get LDAP config |
| PUT | `/api/platform/sites/{siteId}/ldap-config` | Upsert LDAP config |
| POST | `/api/platform/sites/ldap-test` | Test LDAP connection |
| GET | `/api/platform/sites/{siteId}/theme` | Get theme |
| PUT | `/api/platform/sites/{siteId}/theme` | Upsert theme |
| GET | `/api/platform/role-templates` | List role templates |
| POST | `/api/platform/role-templates` | Create template |
| PUT | `/api/platform/role-templates/{id}` | Update template |
| POST | `/api/platform/role-templates/{id}/publish` | Publish → clone to all sites |
| GET | `/api/platform/role-templates/permission-catalog` | List all permission keys |

### Site Business Admin (`[SiteBusinessAdmin]`)

| Method | Path | Description |
| ------ | ---- | ----------- |
| GET | `/api/site/users` | List users in own site |
| POST | `/api/site/users` | Create user (pre-provision) |
| GET | `/api/site/users/{id}` | Get user with roles + direct perms |
| PUT | `/api/site/users/{id}` | Update user |
| POST | `/api/site/users/{id}/disable` | Disable user |
| POST | `/api/site/users/assign-role` | Assign role (with optional expiry) |
| POST | `/api/site/users/revoke-role` | Revoke role |
| POST | `/api/site/users/grant-permission` | Grant direct permission (≤90d, with reason) |
| POST | `/api/site/users/revoke-permission` | Revoke direct permission |
| POST | `/api/site/users/sync` | Trigger LDAP sync |

### Audit (both platform and site admins)

| Method | Path | Description |
| ------ | ---- | ----------- |
| GET | `/api/audit/logs?from=&to=&action=&actorUserId=&outcome=&skip=&take=` | Query audit logs (scoped to user's site or platform-wide) |

---

## Front-End Architecture

### Stack

- **React 19** + **TypeScript 6** (strict mode enabled in tsconfig)
- **Vite 8** (build tool, dev server)
- **Tailwind CSS 4** (utility-first styling)
- **Zustand 4** (state management — auth + theme stores)
- **Axios** (HTTP client with 401-refresh-token interceptor)
- **Sonner** (toast notifications)
- **Lucide React** (icons)
- **Radix UI** (accessible primitives — used in-house, no external UI lib at runtime)

### State Management

- `authStore` — access/refresh tokens, profile, theme bundle (persisted to localStorage)
- `themeStore` — light/dark mode preference (persisted to localStorage)

### Theme Application

The `ThemeApplier` component reads `authStore.theme` (the brand palette from
the user's site) and `themeStore.isDark` (the user's preferred mode), then
writes CSS variables (`--color-primary`, `--color-accent`, etc.) onto the
`<html>` element. Users can toggle light/dark independently; the brand
palette is determined by their site.

### Routing

```
/login                  — anonymous, public
/dashboard              — any authenticated user
/platform/sites         — Platform Admin only
/platform/role-templates — Platform Admin only
/site/users             — Site Business Admin (or Platform Admin)
/audit/logs             — Platform Admin (sees all) or Site Admin (own site only)
/site/audit             — alias for /audit/logs (site-scoped)
/settings               — any authenticated user
```

---

## Security Model

### Authentication

1. **Platform Admin** — Local bcrypt credential in `PlatformUsers` table.
   Must work even if all site LDAPs are down.
2. **Site Users** — LDAP bind with user's own credentials. LDAP server
   must use LDAPS (port 636) or StartTLS (port 389). Plain 389 is
   rejected at save time.
3. **JWT** — HS256 signed, 15-minute lifetime, carries:
   - `sub` (user ID), `email`, `display_name`
   - `scope` (platform | site)
   - `site_id`, `site_code` (null for platform)
   - `perm_ver` (for stale-perm detection)
   - `role[]` claims
   - `perm[]` claims (each effective permission)
   - `is_platform_admin` / `is_site_admin` boolean flags

### Refresh Token

- Opaque 256-bit random string, stored as SHA-256 hash in DB.
- 24-hour lifetime, rotating on use.
- Revocable (logout, admin force-revoke).
- Tracked server-side per user — stolen tokens can be invalidated.

### Authorization

- **Fail-closed** — Anonymous requests are denied by default.
- **Permission-based** — `[HasPermission("user.write")]` checks JWT claim.
- **Platform Admin bypass** — Platform admin tokens bypass site-level
  permission checks.
- **Tenant isolation** — Site Business Admins can NEVER touch users in
  another site (enforced by `SiteDbContextFactory` resolving the
  connection string from JWT `site_id` claim).

### LDAP Credential Storage

- Bind DN + bind password are encrypted via ASP.NET Core Data Protection
  (DPAPI) before being persisted to the platform DB.
- The DPAPI key ring is persisted to disk (configurable path).
- Decryption happens only in-memory at sync time.

### Rate Limiting

- **Default policy** — 500 requests/min per IP.
- **Auth policy** — 20 requests/min per IP for `/api/auth/*` endpoints.
- **User-level lockout** — After 5 failed logins per IP+email, the
  account is locked for 15 minutes.

### CORS

- **Fail-closed in Production** — If `Cors:AllowedOrigins` is empty,
  all cross-origin requests are rejected.
- **DevOrigins** — A separate list for development-only origins
  (localhost), ignored in Production.

### HTTPS / HSTS

- `UseHttpsRedirection` + `UseHsts` enabled in Production.
- `RequireHttpsMetadata = true` for JWT validation in Production.

---

## Audit & Compliance

### Audit Log Properties

- **Append-only** — UPDATE and DELETE rejected at the EF Core SaveChanges
  pipeline level (`RejectAuditLogMutation` in PlatformDbContext and
  SiteDbContext).
- **Hash-chained** — Each entry's `Hash` is SHA-256 of
  `PreviousHash + canonical JSON of the entry`. Any retroactive tampering
  is detectable by recomputing the chain.
- **Denormalized actor info** — `ActorEmail`, `ActorIp`, `ActorUserAgent`
  are stored on each entry so forensic queries work even after user
  deletion.
- **Dual scope** — Platform-level actions go to `syntera_master.AuditLogs`;
  site-level actions go to the site's `AuditLogs` table.

### Retention

- Default: **10 years** (configurable via `PlatformSettings:AuditRetentionYears`).
- Past retention, entries are archived to cold storage (Azure Blob Cool
  tier) by a monthly background job and pruned from the hot table.
- Archived entries kept indefinitely for compliance.

### Audit Events Captured

| Event | Trigger |
| ----- | ------- |
| `auth.login` | Login attempt (success or failure) |
| `site.create` / `site.update` / `site.disable` | Platform admin site actions |
| `ldap.write` / `ldap.test_connection` | LDAP config changes |
| `theme.write` | Theme palette changes |
| `role_template.create` / `role_template.publish` | Role template lifecycle |
| `business_admin.assign` / `business_admin.revoke` | Delegation changes |
| `user.create` / `user.update` / `user.disable` | Site user lifecycle |
| `user_role.assign` / `user_role.revoke` | Role assignment changes |
| `permission.grant` / `permission.revoke` | Direct permission changes |
| `user.sync` | LDAP sync runs |

---

## Brand Theming

Each site has a `SiteTheme` record in the platform DB:

```json
{
  "themeKey": "kalventis-navy",
  "lightPaletteJson": "{\"primary\":\"#0B3D6F\",\"accent\":\"#00A7B5\",...}",
  "darkPaletteJson":  "{\"primary\":\"#60A5FA\",\"accent\":\"#22D3EE\",...}",
  "logoUrl": null
}
```

### Default Palettes

| Site | Light Primary | Light Accent | Dark Primary | Dark Accent |
| ---- | ------------- | ------------ | ------------ | ----------- |
| Syntera (default) | `#0B3D6F` navy | `#00A7B5` teal | `#60A5FA` | `#22D3EE` |
| Kalventis | (configured per site) | | | |
| Kalbe | | | | |
| Dankos | | | | |
| Hexpharm | | | | |
| Fima | | | | |
| GOF | | | | |

### Performance

- Themes are cached in-memory for 5 minutes (`IMemoryCache`).
- Cache invalidated on theme update via `IThemeService.InvalidateCacheAsync`.
- No per-request DB hit on the hot path (login → theme → response).

### User Override

Users can toggle between light/dark mode via the header toggle. The
preference is stored in `localStorage` (key: `syntera.theme`). The brand
palette is NOT user-overridable — it is determined by the user's site.

---

## Operational Runbook

### Adding a New Site

1. **Platform Admin** logs in to the UI.
2. Navigate to **Sites** → **New Site**.
3. Fill in:
   - Code (e.g., `newsite`)
   - Display Name (e.g., `PT New Site Pharma`)
   - Database Connection String (SQL Server)
   - Email Domains (e.g., `newsite.com`)
   - Default Theme Key
4. Save. The site is created in the platform DB.
5. **Configure LDAP** for the new site:
   - Host, Port (636 for LDAPS, 389 for StartTLS)
   - Base DN (e.g., `DC=NEWSITE,DC=DOM`)
   - Email Attribute (default: `userPrincipalName`)
   - Bind DN + Bind Password (service account for sync)
   - User Filter Template
6. **Test LDAP Connection** with a real user email.
7. **Configure Theme** palette (light + dark JSON).
8. The site's database must be created in SQL Server (manually or via
   provisioning pipeline). The schema is created on first access.
9. **Publish Role Templates** — they auto-clone into the new site.

### Delegating a Site Business Admin

1. Platform Admin creates a User row in the site database (via API or
   after the Business Admin's first LDAP login).
2. Assign the `site-business-admin` role to that user.
3. The user can now log in and manage users in their site.

### Triggering LDAP Sync

Site Business Admin can trigger sync from the UI:

1. Navigate to **Users** → **Sync LDAP**.
2. Confirm the action.
3. The sync job:
   - Searches LDAP for all active users
   - Creates new users in the site DB
   - Updates display names if changed
   - Disables users no longer in LDAP
4. Result summary shown via toast.

### Recovering from LDAP Outage

If a site's LDAP is down:

- All users from that site cannot log in (no fallback, per requirement).
- **Platform Admin** can still log in (`admin@syntera.com` uses local credential).
- Platform Admin can disable the site (`POST /api/platform/sites/{id}/disable`)
  to prevent confusion, then re-enable when LDAP is back.

### Rotating the JWT Signing Key

1. Generate a new key (min 32 chars, random).
2. Set via environment variable: `SYNTERA_Jwt__SigningKey=<new-key>`.
3. Restart the API.
4. All existing JWTs become invalid — users will be force-logged-out.
   Their refresh tokens still work (the refresh flow issues new JWTs
   signed with the new key).

### Backup & Restore

- **Platform DB** — Back up daily. Contains all site configs, role
  templates, platform admin credentials, and platform audit logs.
- **Site DBs** — Back up independently per site. Restore one site
  without affecting others.
- **DPAPI key ring** — Back up the directory at `DataProtection:KeyPath`.
  If lost, all encrypted LDAP bind passwords become undecryptable;
  Platform Admin must re-enter them.
