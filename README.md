# 🌿 Syntera IAM

> **One Platform. One Standard. One Direction.**
>
> A multi-tenant **Identity & Access Management** platform for the Syntera /
> Kalbe / Dankos / Hexpharm / Fima / GOF / Kalventis pharmaceutical group.
> Built on **.NET 10 + React 19 + SQL Server 2022**, themed around each
> site's brand identity.

This repository contains a centralized **IAM platform** that authenticates
users from 6 affiliated sites via their respective LDAP directories, manages
role-based access control with delegated administration, and provides a
tamper-evident audit trail for compliance (CFR Part 11 / GxP).

---

## 📑 Table of Contents

1. [Monorepo Layout](#monorepo-layout)
2. [Architecture Overview](#architecture-overview)
3. [Login Flow with LDAP Domain Routing](#login-flow-with-ldap-domain-routing)
4. [Permission Model (Hybrid RBAC + Direct Permission)](#permission-model-hybrid-rbac--direct-permission)
5. [Multi-Tenant Database Architecture](#multi-tenant-database-architecture)
6. [4-Tier Admin Delegation](#4-tier-admin-delegation)
7. [Quick Start](#quick-start)
8. [Syntera.DbSetup](#synteradbsetup)
9. [Configuration & Secrets](#configuration--secrets)
10. [Running the Apps](#running-the-apps)
11. [Project Layout](#project-layout)
12. [API Surface](#api-surface)
13. [Front-End Architecture](#front-end-architecture)
14. [Security Model](#security-model)
15. [Audit & Compliance](#audit--compliance)
16. [Brand Theming](#brand-theming)
17. [Operational Runbook](#operational-runbook)

> **New to this project?** Start with **[SETUP.md](./SETUP.md)** — a step-by-step
> guide from zero to a working login (SQL Server, migrations, seeding, first
> Platform Admin login). Includes troubleshooting for common issues like
> "More than one DbContext was found" and login 401 errors.

---

## Monorepo Layout

Syntera is a **single monorepo with three projects**:

| Project | Path | Type | Stack | Purpose |
| ------- | ---- | ---- | ----- | ------- |
| `Syntera.Backend` | `Syntera.Backend/` | Web API | .NET 10 ASP.NET Core | REST API + EF Core 10 + JWT + LDAP + SQL Server 2022 |
| `Syntera.React` | `Syntera.React/` | SPA | React 19 + Vite 8 + Tailwind v4 | Admin UI front-end |
| `Syntera.DbSetup` | `Syntera.DbSetup/` | Console | .NET 10 | One-shot DB creation + migration + seed tool |

`Syntera.DbSetup` references `Syntera.Backend` and reuses its
`PlatformDbContext`, `SiteDbContext`, `DbSeeder`, entities, and migrations —
no code duplication.

---

## Architecture Overview

**Key design principles:**

1. **Multi-tenant by database isolation** — One platform database
   (`syntera_master`) + one isolated database per site
   (`syntera_kalventis`, `syntera_kalbe`, `syntera_fima`, `syntera_gof`,
   `syntera_dankos`, `syntera_hexpharm`). A compromise of Site A's database
   never exposes Site B's data. **7 databases total** (1 master + 6 sites).

2. **Email-domain routing** — Users log in with a single form; the platform
   routes authentication to the correct LDAP server based on the email's
   domain (`@kalventis.com` → LDAP Kalventis, `@kalbe.co.id` → LDAP Kalbe, …).

3. **4-tier delegated administration** — Platform Admin (`admin@syntera.com`)
   → System Admin (per site) → Site Business Admin → End Users. System Admin
   is the only role (besides Platform Admin) that can assign Business Admin
   for their site.

4. **Hybrid RBAC + Direct Permission** — Users receive permissions from
   assigned roles PLUS direct grants (with mandatory expiry, reason, and
   approver) for temporary elevated access. Direct permissions auto-revoke
   at expiry (max 90 days).

5. **Tamper-evident audit log** — Append-only, hash-chained entries.
   UPDATE/DELETE rejected at the EF Core `SaveChanges` pipeline level.
   Retention configurable (default 10 years) for CFR Part 11 compliance.

6. **DB-stored brand themes** — Each site's palette (light + dark) is
   stored as JSON in the platform DB and cached in-memory on the API.
   Platform Admin can update brand colors without redeploying.

### Architecture Diagrams

Four architecture diagrams are stored in `docs/diagrams/`:

| Diagram | File | Description |
| ------- | ---- | ----------- |
| Login Flow | `01_login_flow.png` | Email domain → LDAP routing → JWT issuance |
| Permission Model | `02_permission_model.png` | Hybrid RBAC + Direct Permission with expiry |
| Multi-Tenant DB | `03_multi_tenant_db.png` | Platform DB + per-site DB isolation |
| Role Hierarchy | `04_role_hierarchy.png` | 4-tier admin delegation with permission scope |

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
LDAP direct bind with user's own credentials (LDAPS or StartTLS — never plain 389)
        ↓
Pre-provisioning check (user must exist in site DB)
        ↓
Issue JWT (15 min) + Refresh Token (platform 1d / site 7d, rotating, httpOnly cookie)
Apply site theme (light/dark from user preference)
Write audit log (immutable, hash-chained)
        ↓
✓ Authenticated → redirect to /dashboard
```

**Key points:**

- **No fallback** — If LDAP is down, login fails. Platform Admin
  (`@syntera.com`) is the only user that can log in without LDAP.
- **Direct bind (no service account)** — The backend uses the user's own
  email + password to bind to LDAP (`NovellLdapClient.AuthenticateAsync`).
  `SiteLdapConfig` does NOT store a bind password; only host, port,
  `BaseDn`, and optional `UpnDomain` for bind-DN transformation.
- **Pre-provisioning required** — Even after LDAP authentication succeeds,
  the user must exist in the site database (provisioned by the Site Business
  Admin) before they can access the platform. This prevents unauthorized
  users from any LDAP from logging in.
- **LDAP injection protection** — User email is escaped per RFC 4515 before
  being inserted into the LDAP filter (`* ( ) \ NUL` and bytes < 0x20).
- **2-bucket rate limiting** — Per `IP+email` AND per `email` alone (window
  15 min, 5 attempts). After 5 failed attempts, the account is locked for
  15 minutes (configurable via platform settings).
- **Auto-sync on login** — `DisplayName` and `Title` are auto-synced from
  LDAP to the site DB on successful login (only if LDAP returned non-null
  values that differ from stored values).

---

## Permission Model (Hybrid RBAC + Direct Permission)

```
effective = role_permissions(user) ∪ direct_permissions(user, not_expired, not_revoked)
denied    = explicit_deny_grants(user)
final     = effective \ denied
```

### Permission Sources

1. **RBAC path (stable, audit-friendly):**
   `User → UserRole → Role → RolePermission → Permission`

2. **Direct permission path (temporary, with mandatory expiry):**
   `User → UserPermission → Permission`
   Every direct grant MUST have:
   - `Reason` (required text, min 10 chars, max 500)
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
| Delegation | `business_admin.assign`, `business_admin.revoke`, `system_admin.assign`, `system_admin.revoke` |
| Platform Audit | `platform.audit.read`, `platform.config.read`, `platform.config.write` |
| Platform Users | `platform_user.read`, `platform_user.create`, `platform_user.update`, `platform_user.disable` |
| User Management (Site) | `user.read`, `user.write`, `user.disable` |
| Role Assignment (Site) | `role.read`, `user_role.assign`, `user_role.revoke` |
| Permission Grants (Site) | `permission.read`, `permission.grant`, `permission.revoke` |
| Site Audit | `audit.read`, `report.read` |
| Common | `dashboard.read`, `profile.read` |

### Permission Catalog (Static)

`PermissionService.GetCatalogAsync()` returns a `PermissionCatalog.Static`
with 11 groups:

1. Site Management
2. LDAP Configuration
3. Theme Management
4. Role Templates
5. Delegation
6. Platform Audit
7. Platform Users
8. User Management (Site)
9. Role Assignment (Site)
10. Permission Grants (Site)
11. Site Audit

Platform Admin's effective permission set is a hardcoded array of 19
platform-level keys (returned by `GetPlatformAdminPermissions()`).

### Permission Cache

- In-memory `IMemoryCache` per user with 5-min TTL.
- Cache key: `perm:{userId}:{siteContextId}`.
- Cache invalidation: `User.PermissionsVersion` is bumped on any role/permission
  change. The JWT carries this version; if it doesn't match the current value
  on a request, the permission engine re-resolves the effective set and the
  client must refresh its token.

### Authorization Attributes (Backend)

| Attribute | Behavior |
| --------- | -------- |
| `[HasPermission("user.write")]` | Checks JWT `perm` claim for the specific key. Platform Admin bypasses (claim `is_platform_admin=true`). |
| `[PlatformAdminOnly]` | Only `admin@syntera.com` (claim `is_platform_admin=true`). |
| `[SiteBusinessAdmin]` | Platform Admin, OR claim `is_site_admin=true`, OR role `system-admin`. |
| `[PlatformAdminOrSystemAdmin]` | Platform Admin OR role `system-admin`. Used for `GET /api/platform/sites` and audit logs. |

For endpoints gated on a specific System Admin scope (Business Admin
assign/list/revoke for a specific site), the controller does a manual
`_current.Roles.Contains("system-admin") && _current.SiteId == siteId`
check (no dedicated `[SystemAdminOnly]` attribute).

---

## Multi-Tenant Database Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Application Tier — single .NET 10 API process                   │
│                                                                  │
│  ISiteDbContextFactory ──── resolves correct DbContext           │
│         ↓ based on JWT site_id claim                             │
│  PlatformDbContext (scoped) ── always points to master          │
│  SiteDbContext (scoped per request) ── per-site connection       │
└─────────────────────────────────────────────────────────────────┘
        ↓
┌─────────────────────────────────────────────────────────────────┐
│ Database Tier — 7 databases total                                │
│                                                                  │
│  syntera_master (PLATFORM)                                       │
│    ├── Sites, SiteLdapDomains, SiteLdapConfigs, SiteThemes        │
│    ├── RoleTemplates, RoleTemplatePermissions                    │
│    ├── PlatformUsers, RefreshTokens (platform scope)              │
│    ├── PlatformSettings (audit retention, token lifetimes)       │
│    └── AuditLogs (platform-level)                                │
│                                                                  │
│  syntera_kalventis   syntera_kalbe   syntera_dankos               │
│  syntera_hexpharm    syntera_fima    syntera_gof                  │
│    Each contains:                                                │
│    ├── Users, Roles, Permissions, UserRoles, RolePermissions    │
│    ├── UserPermissions (direct grants)                           │
│    ├── RefreshTokens (site scope)                                 │
│    ├── AuditLogs (site-level)                                     │
│    └── UserSyncHistory                                            │
└─────────────────────────────────────────────────────────────────┘
```

### Isolation Guarantees

- **Connection-level isolation** — Each `SiteDbContext` uses its own
  connection string resolved at runtime from the platform DB's `Sites`
  table; no shared connection pool.
- **No cross-site JOIN** — Forbidden at the repository layer. Cross-site
  aggregation done in application code.
- **Backup / Restore per-site** — Each DB backed up independently; site
  outage does not affect others.
- **Migrations history table** — `__EFMigrationsHistory_Platform` for the
  master DB, `__EFMigrationsHistory_Site` (shared table name) for all
  site DBs.

### DbContexts

| DbContext | Scope | Tables |
| --------- | ----- | ------ |
| `PlatformDbContext` | Singleton connection to `syntera_master` | Sites, LdapDomains, LdapConfigs, Themes, RoleTemplates, RoleTemplatePermissions, PlatformUsers, RefreshTokens, AuditLogs, Settings |
| `SiteDbContext` | Scoped per request, connection resolved from JWT `site_id` claim | Users, Roles, Permissions, UserRoles, RolePermissions, UserPermissions, AuditLogs, UserSyncHistory, RefreshTokens |

### DbSets per DbContext

**`PlatformDbContext`** (`syntera_master`):

| DbSet | Entity |
| ----- | ------ |
| `Sites` | `Site` |
| `LdapDomains` | `SiteLdapDomain` |
| `LdapConfigs` | `SiteLdapConfig` |
| `Themes` | `SiteTheme` |
| `RoleTemplates` | `RoleTemplate` |
| `RoleTemplatePermissions` | `RoleTemplatePermission` |
| `PlatformUsers` | `PlatformUser` |
| `RefreshTokens` | `RefreshToken` |
| `AuditLogs` | `AuditLog` |
| `Settings` | `PlatformSetting` |

**`SiteDbContext`** (`syntera_{site_code}`):

| DbSet | Entity |
| ----- | ------ |
| `Users` | `User` (soft-delete) |
| `Roles` | `Role` (soft-delete) |
| `Permissions` | `Permission` |
| `UserRoles` | `UserRole` |
| `RolePermissions` | `RolePermission` |
| `UserPermissions` | `UserPermission` |
| `AuditLogs` | `AuditLog` |
| `UserSyncHistory` | `UserSyncHistory` |
| `RefreshTokens` | `RefreshToken` |

### Migrations

**`Data/Migrations/Platform/`** (history table `__EFMigrationsHistory_Platform`):

| File | Migration Name |
| ---- | -------------- |
| `20260827082005_InitialPlatform.cs` | `InitialPlatform` |
| `20260828021319_AddUpnDomain.cs` | `AddUpnDomain` |
| `20260904070538_AddRefreshTokenFamilyId.cs` | `AddRefreshTokenFamilyId` |

**`Data/Migrations/Site/`** (history table `__EFMigrationsHistory_Site`):

| File | Migration Name |
| ---- | -------------- |
| `20260827082016_InitialSite.cs` | `InitialSite` |
| `20260904044709_AddUserTitle.cs` | `AddUserTitle` |
| `20260904070546_AddRefreshTokenFamilyId.cs` | `AddRefreshTokenFamilyId` |

> **Note on baseline:** The earliest IAM builds created the Platform DB via
> `EnsureCreatedAsync()` — schema-complete but without migration history.
> `DatabaseInitializer.MigrateOrBaselineAsync` detects this state on Dev
> startup and baselines all existing migrations as already-applied (without
> replay), preserving existing data (admin, sites, themes, audit trail).
> Fresh installs and incremental migrations still follow the normal path.

---

## 4-Tier Admin Delegation

The role hierarchy is **4-tier**, documented in `DbSeeder.cs`:

```
Platform Admin → System Admin (per site) → Site Business Admin → End Users
```

### Tier 1 — Platform Admin (`admin@syntera.com`)

- Local bcrypt credential (must work even if all site LDAPs are down)
- Stored in `syntera_master.PlatformUsers`
- Permissions (static, hardcoded in `PermissionService`): all 19 platform
  keys (`site.*`, `ldap.*`, `theme.*`, `role_template.*`, `platform.*`,
  `platform_user.*`)
- **Cannot:** read site business data, view individual users, assign
  roles to end users (only delegate to system admins)
- Can change own password via `POST /api/auth/change-password` (enforced
  by `PasswordPolicy`).

### Tier 2 — System Admin (per site)

- Assigned by **Platform Admin** via `POST /api/platform/sites/{siteId}/system-admin`
- Authenticated via site LDAP, scoped to own site DB
- Permissions (from `system-admin` template): `dashboard.read`, `audit.read`,
  `profile.read`, `business_admin.assign`, `business_admin.revoke`
- **Can:** assign/revoke Business Admin for their own site
- **Cannot:** create role templates, modify LDAP config, edit site
  `DisplayName` or `LdapDomains`, manage other System Admins

### Tier 3 — Site Business Admin (per site)

- Assigned by **System Admin** (or **Platform Admin** for bootstrapping)
- Authenticated via site LDAP, scoped to own site DB
- Permissions (from `site-business-admin` template): `user.*`, `role.read`,
  `user_role.assign`, `user_role.revoke`, `permission.read`,
  `permission.grant`, `permission.revoke`, `audit.read`, `report.read`
- **Cannot:** create role templates, modify LDAP config, view other
  sites, escalate to Platform Admin

### Tier 4 — End Users (per site)

- Authenticated via site LDAP
- Permissions come from assigned roles + direct grants
- Standard seeded roles:
  - `viewer` — read-only (`dashboard.read`, `audit.read`, `profile.read`)
  - `eng-planner` — `dashboard.read`, `audit.read`, `report.read`, `profile.read`
  - `supervisor` — same as `eng-planner`
  - `technician` — `dashboard.read`, `profile.read`
  - `eng-manager` — has `IsSiteAdminRole=true`; can manage users like
    a Business Admin but not assign the Business Admin role itself
  - `qo-manager` — Quality Operations Manager; `dashboard.read`,
    `audit.read`, `report.read`, `profile.read`
- Direct permission override possible with expiry (max 90 days)

### Default Role Templates (seeded by `DbSeeder`)

| Key | Display Name | IsSiteAdminRole | Description |
| --- | ------------ | --------------- | ----------- |
| `system-admin` | System Administrator | no | Tier 2; assigns Business Admin |
| `site-business-admin` | Site Business Administrator | **yes** | Tier 3; manages users/roles/permissions |
| `viewer` | Viewer | no | Read-only dashboards + own profile |
| `eng-planner` | Eng Planner | no | Dashboard + audit + reports |
| `supervisor` | Supervisor | no | Dashboard + audit + reports |
| `technician` | Technician | no | Dashboard access only |
| `eng-manager` | Eng Manager | **yes** | Manages users in own site |
| `qo-manager` | QO Manager | no | Audit + reports |

All 8 templates are seeded as **Published** (`IsPublished=true`, `Version=1`)
on first run. Site Admin roles (`isSiteAdminRole=true`) unlock the
`is_site_admin=true` JWT claim for that user.

---

## Quick Start

### Prerequisites

- .NET 10 SDK
- Node.js 24+ (or Bun)
- SQL Server 2022 (local, remote, or Docker)

### One-Command Setup (recommended)

```bash
./setup.sh
```

This script:

1. Validates prerequisites (.NET 10 SDK).
2. Sets up user-secrets on `Syntera.Backend` (prompts for SQL Server
   connection string, admin password; auto-generates JWT signing key).
3. Sets connection strings for all 6 sites (`ConnectionStrings:Sites:{code}`).
4. Runs `Syntera.DbSetup` to create 7 databases + apply migrations + seed
   platform data (admin, sites, role templates, themes).
5. Starts Backend briefly to verify login works (`admin@syntera.com`).

### Manual Setup

If you prefer to set up step-by-step, see **[SETUP.md](./SETUP.md)** for
the full walkthrough.

---

## Syntera.DbSetup

`Syntera.DbSetup` is a **console project** (output type `Exe`) that
one-shots all database work. It is invoked by `setup.sh`, `setup-db.sh`,
or run directly.

### What it does (3 steps)

1. **Step 1 — Platform database:** Ensures `syntera_master` exists
   (via `CREATE DATABASE` against the server's `master` DB), then runs
   `PlatformDbContext.Database.MigrateAsync()` (history table
   `__EFMigrationsHistory_Platform`).
2. **Step 2 — Site databases:** Iterates over `ConnectionStrings:Sites`
   from `appsettings.json` (6 entries: `kalventis`, `kalbe`, `fima`,
   `gof`, `dankos`, `hexpharm`). For each: ensures the DB exists, then
   runs `SiteDbContext.Database.MigrateAsync()` (history table
   `__EFMigrationsHistory_Site`).
3. **Step 3 — Seed:** Calls `DbSeeder.SeedPlatformAsync` which seeds
   default platform settings, 8 role templates, 6 sites (with themes
   + LDAP domains), and the Platform Admin user.

### Usage

```bash
# Via wrapper script (Development environment, idempotent)
./setup-db.sh
# or for Production
./setup-db.sh Production

# Directly
cd Syntera.DbSetup
dotnet run
```

### Output example

```
[14:35:01 INF] ════════════════════════════════════════════════════════════════
[14:35:01 INF]   Syntera DbSetup — creating all databases & applying migrations
[14:35:01 INF] ════════════════════════════════════════════════════════════════
[14:35:01 INF] ▶ Step 1/3: Platform database (syntera_master)
[14:35:01 INF]   Ensuring database 'syntera_master' exists...
[14:35:02 INF]   ✓ Database 'syntera_master' ready
[14:35:02 INF]   Applying PlatformDbContext migrations...
[14:35:03 INF]   ✓ Platform migrations applied
[14:35:03 INF] ▶ Step 2/3: Site databases (6 sites)
[14:35:03 INF]   [KALVENTIS]  ✓ kalventis migrations applied
[14:35:04 INF]   [KALBE]      ✓ kalbe migrations applied
[14:35:04 INF]   [FIMA]       ✓ fima migrations applied
[14:35:04 INF]   [GOF]        ✓ gof migrations applied
[14:35:04 INF]   [DANKOS]     ✓ dankos migrations applied
[14:35:04 INF]   [HEXPHARM]   ✓ hexpharm migrations applied
[14:35:15 INF] ▶ Step 3/3: Seed platform data (admin user, role templates, 6 sites, themes)
[14:35:15 INF]   ✓ Seeding complete
[14:35:15 INF] ════════════════════════════════════════════════════════════════
[14:35:15 INF]   ✓ All databases ready!
[14:35:15 INF]   Platform DB: syntera_master (11 tables)
[14:35:15 INF]   Site DB kalventis : syntera_kalventis (9 tables)
[14:35:15 INF]   ...
[14:35:15 INF]   Platform Admin: admin@syntera.com
[14:35:15 INF]   Next: cd ../Syntera.Backend && dotnet run
[14:35:15 INF] ════════════════════════════════════════════════════════════════
```

### How DbSetup reuses Backend code

`Syntera.DbSetup.csproj` includes:

```xml
<ProjectReference Include="..\Syntera.Backend\Syntera.Backend.csproj" />

<!-- Copy appsettings.json from the Backend so DbSetup reads the same config -->
<None Include="..\Syntera.Backend\appsettings.json" Link="appsettings.json"
      CopyToOutputDirectory="PreserveNewest" />
<None Include="..\Syntera.Backend\appsettings.Development.json"
      Link="appsettings.Development.json"
      CopyToOutputDirectory="PreserveNewest" />
```

So all configuration (connection strings, sites seed, theme palettes)
comes from the Backend's `appsettings.json`. User-secrets and
environment variables (`SYNTERA_*`) are also honored.

---

## Configuration & Secrets

### Backend (`appsettings.json`)

| Key | Description | Required in Production |
| --- | ----------- | ---------------------- |
| `ConnectionStrings:Platform` | SQL Server connection string for `syntera_master` | ✅ Yes |
| `ConnectionStrings:Sites:{code}` | Connection string for each of the 6 site DBs (`kalventis`, `kalbe`, `fima`, `gof`, `dankos`, `hexpharm`) | ✅ Yes |
| `Sites[]` | Seed array for 6 sites: `Code`, `DisplayName`, `EmailDomain`, `LightPalette`, `DarkPalette` | Optional (used by seeder) |
| `Jwt:SigningKey` | HS256 signing key, min 32 chars | ✅ Yes (`SYNTERA_Jwt__SigningKey`) |
| `Jwt:AccessTokenMinutes` | JWT lifetime (default 15) | Optional |
| `Jwt:RefreshTokenDays` | Legacy refresh token lifetime (default 1) | Optional (fallback) |
| `Jwt:RefreshTokenDaysPlatform` | Refresh token TTL for platform scope (default 1) | Optional |
| `Jwt:RefreshTokenDaysSite` | Refresh token TTL for site scope (default 7) | Optional |
| `Cors:AllowedOrigins` | Comma-separated list of allowed origins | ✅ Yes (fail-closed if empty in Production) |
| `Cors:DevOrigins` | Development-only origins (default `http://localhost:5173`, `http://localhost:4173`) | Optional |
| `DataProtection:KeyPath` | Directory for DPAPI key ring | Optional (default `/var/lib/syntera/keys`) |
| `Seed:PlatformAdminEmail` | Platform admin email (default `admin@syntera.com`) | Optional |
| `Seed:PlatformAdminPassword` | Initial platform admin password | ✅ Yes (`SYNTERA_Seed__PlatformAdminPassword`) |
| `Audit:RetentionYears` | Audit log retention (default 10) | Optional |
| `Audit:EnforceRetention` | Enable retention sweeper (default `false`) | Optional |
| `PasswordPolicy:MinLength` | Min password length (default 12) | Optional |
| `PasswordPolicy:MaxLength` | Max password length (default 256) | Optional |
| `PasswordPolicy:RequireUpper/Lower/Digit/Symbol` | Composition rules (all default `true`) | Optional |
| `Ldap:SkipCertValidation` | Skip TLS cert validation (default `false`; Dev `true`) | Optional |
| `Ldap:AllowPlainPort389` | Allow cleartext LDAP port 389 without StartTLS (default `false`; Dev `true`) | Optional |
| `Serilog:MinimumLevel` | Default `Information` | Optional |
| `Serilog:WriteTo` | Console + File (`logs/syntera-api-.log`, daily rolling, 30 days) | Optional |

### `appsettings.Development.json` (Dev overrides)

| Key | Dev value | Base value |
| --- | --------- | ---------- |
| `Logging:LogLevel:Default` | `Debug` | `Information` |
| `ConnectionStrings:Platform` password | `Passwordkuat123!` | `__SET_VIA_ENV_OR_USER_SECRETS__` |
| `ConnectionStrings:Sites:*` password | `Passwordkuat123!` | `__SET_VIA_ENV_OR_USER_SECRETS__` |
| `Jwt:AccessTokenMinutes` | `60` | `15` |
| `Jwt:RefreshTokenDays` | `7` | `1` |
| `Jwt:SigningKey` | `DEV_ONLY_DO_NOT_USE_IN_PRODUCTION_32_CHARS_MIN_2026` | `""` |
| `Seed:PlatformAdminPassword` | `ChangeMe!Strong#1` | `""` |
| `Audit:RetentionYears` | `1` | `10` |
| `Audit:EnforceRetention` | `true` | `false` |
| `Ldap:SkipCertValidation` | `true` | `false` |
| `Ldap:AllowPlainPort389` | `true` | `false` |

### Fail-Fast Startup Checks (Production)

`Program.cs` refuses to start in Production if any of these conditions are true:

1. `Jwt:SigningKey` missing or shorter than 32 chars.
2. `Cors:AllowedOrigins` missing or empty.
3. `Seed:PlatformAdminPassword` missing.
4. `ConnectionStrings:Platform` contains the placeholder `__SET_VIA_ENV`.
5. `Ldap:AllowPlain=true` (plain LDAP forbidden in production) — *additional
   runtime check inside `NovellLdapClient`* (port 389 without StartTLS
   rejected unless `Ldap:AllowPlainPort389=true` or a debugger is attached).

### Environment Variable Convention

All config keys can be overridden via environment variables using `__`
(double underscore) as the hierarchy separator, prefixed with `SYNTERA_`:

```bash
SYNTERA_ConnectionStrings__Platform="Server=..."
SYNTERA_ConnectionStrings__Sites__kalventis="Server=..."
SYNTERA_Jwt__SigningKey="..."
SYNTERA_Seed__PlatformAdminPassword="..."
SYNTERA_Audit__RetentionYears="7"
SYNTERA_Ldap__AllowPlainPort389="false"
```

### User-Secrets (Development only)

```bash
cd Syntera.Backend
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Platform" "Server=...,1433;Database=syntera_master;User Id=sa;Password=...;TrustServerCertificate=True;MultipleActiveResultSets=True"
dotnet user-secrets set "ConnectionStrings:Sites:kalventis" "Server=...,1433;Database=syntera_kalventis;User Id=sa;Password=...;TrustServerCertificate=True;MultipleActiveResultSets=True"
# ... (one per site)
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48 | tr -d '/+=' | head -c 48)"
dotnet user-secrets set "Seed:PlatformAdminPassword" "ChangeMe!Strong#1"
```

`setup.sh` does all of the above interactively.

---

## Running the Apps

### Development

The fastest way to start both backend + frontend:

```bash
./dev.sh            # starts both backend (:5296) + frontend (:5173)
./dev.sh backend    # backend only
./dev.sh frontend   # frontend only
```

Or run them manually in two terminals:

```bash
# Terminal 1: Backend
cd Syntera.Backend
export ASPNETCORE_ENVIRONMENT=Development
dotnet run                       # http://localhost:5296  (Swagger: /docs)

# Terminal 2: Frontend
cd Syntera.React
bun install                      # or: npm install
bun run dev                      # http://localhost:5173  (proxies /api → :5296)
```

### Production

```bash
# Backend
cd Syntera.Backend
dotnet publish -c Release -o ./publish
SYNTERA_Jwt__SigningKey="..." \
SYNTERA_Seed__PlatformAdminPassword="..." \
SYNTERA_Cors__AllowedOrigins="https://iam.syntera.com" \
SYNTERA_ConnectionStrings__Platform="..." \
./publish/Syntera.Backend

# Frontend
cd Syntera.React
bun run build
# Serve dist/ via nginx, Apache, or any static file server.
# Reverse proxy /api/* → backend (no env-var override is available —
# the axios client hardcodes baseURL: "/api").
```

### Ports

| Service | Dev URL |
| ------- | ------- |
| Backend | `http://localhost:5296` |
| Backend (HTTPS profile) | `https://localhost:7160` (also listens on 5296) |
| Swagger UI | `http://localhost:5296/docs` |
| Health check | `http://localhost:5296/health` |
| Frontend | `http://localhost:5173` (strict port) |

Vite dev server proxies `/api/*` → `http://localhost:5296` (see
`Syntera.React/vite.config.ts`). The proxy preserves the `/api` prefix.

---

## Project Layout

```
Syntera/
├── README.md
├── SETUP.md
├── setup.sh                # one-shot: user-secrets + DbSetup + verify login
├── setup-db.sh             # wrapper around Syntera.DbSetup (idempotent)
├── dev.sh                  # starts backend + frontend in dev mode
├── diagnose.sh             # comprehensive diagnostics across the stack
├── docs/
│   └── diagrams/
│       ├── 01_login_flow.png
│       ├── 02_permission_model.png
│       ├── 03_multi_tenant_db.png
│       └── 04_role_hierarchy.png
│
├── Syntera.Backend/        # .NET 10 ASP.NET Core API
│   ├── Program.cs
│   ├── Syntera.Backend.csproj
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── Syntera.Backend.http        # sample HTTP requests (minimal)
│   ├── Authorization/
│   │   └── HasPermissionAttribute.cs   # [HasPermission], [PlatformAdminOnly],
│   │                                    # [SiteBusinessAdmin], [PlatformAdminOrSystemAdmin]
│   ├── Controllers/
│   │   ├── ApiControllerBase.cs        # ApiResponse<T> envelope helpers
│   │   ├── AuthController.cs           # login, refresh, refresh-site, logout, profile, change-password
│   │   ├── SitesController.cs         # site CRUD + LDAP config + theme + system-admin + business-admin
│   │   ├── UsersController.cs          # user CRUD + role/permission assignment
│   │   ├── RoleTemplatesController.cs # template CRUD + publish
│   │   └── AuditLogsController.cs     # audit log query
│   ├── Data/
│   │   ├── PlatformDbContext.cs       # master DB context (10 DbSets)
│   │   ├── SiteDbContext.cs           # per-site DB context (9 DbSets)
│   │   ├── SiteDbContextFactory.cs    # resolves SiteDbContext by JWT site_id
│   │   ├── DatabaseInitializer.cs     # Dev: baseline pre-migrations DBs
│   │   ├── DbSeeder.cs                # seeds platform data on startup
│   │   └── Migrations/
│   │       ├── Platform/              # InitialPlatform, AddUpnDomain, AddRefreshTokenFamilyId
│   │       └── Site/                  # InitialSite, AddUserTitle, AddRefreshTokenFamilyId
│   ├── Extensions/
│   │   └── ServiceCollectionExtensions.cs  # AddSynteraMvc, AddSynteraOpenApi,
│   │                                        # AddSynteraSecurity, AddAuditRetentionSweeper
│   ├── Factories/
│   │   └── DesignTimeDbContextFactories.cs # for `dotnet ef migrations ...` CLI
│   ├── Middleware/
│   │   ├── GlobalExceptionMiddleware.cs    # exception → ApiResponse envelope
│   │   └── SecurityHeadersMiddleware.cs    # CSP, X-Frame-Options, etc.
│   ├── Models/
│   │   ├── ApiResponse.cs                 # ApiResponse<T>, FieldError, PagedResult<T>
│   │   ├── DomainException.cs             # NotFoundException, BusinessRuleException,
│   │   │                                   # AuthenticationException, AuthorizationException
│   │   ├── Dtos/
│   │   │   ├── Auth/AuthDtos.cs            # Login/Refresh/UserProfile/Theme/Logout
│   │   │   ├── Roles/RoleDtos.cs           # RoleTemplate, Permission, PermissionCatalog
│   │   │   ├── Sites/SiteDtos.cs           # Site, LdapConfig, Theme, LdapTest
│   │   │   ├── Users/UserDtos.cs           # User, Role/Permission assignment
│   │   │   └── Validators/AuthValidators.cs # FluentValidation
│   │   └── Entities/
│   │       ├── BaseEntity.cs               # BaseEntity, SoftDeletableEntity
│   │       ├── PlatformEntities.cs         # RoleTemplate, PlatformUser, RefreshToken
│   │       ├── Site.cs                     # Site, SiteLdapDomain, SiteLdapConfig, SiteTheme
│   │       ├── User.cs                     # User, Role, Permission, UserRole, RolePermission, UserPermission
│   │       └── AuditLog.cs                 # AuditLog, UserSyncHistory
│   ├── Properties/
│   │   └── launchSettings.json            # http://localhost:5296
│   └── Services/
│       ├── Interfaces.cs                   # ICurrentUserService, IPasswordHasher
│       ├── AuthService.cs                  # IAuthService — login, refresh, logout, change-password
│       ├── JwtTokenService.cs              # ITokenService + IPasswordHasher (BCrypt)
│       ├── PermissionService.cs            # IPermissionService — RBAC + direct perm resolution
│       ├── AuditService.cs                  # IAuditService — hash-chained audit log writer
│       ├── LdapService.cs                   # ILdapClient + NovellLdapClient — LDAPS / StartTLS
│       ├── ThemeService.cs                  # IThemeService — DB-stored palettes + cache
│       ├── SiteService.cs                  # ISiteManagementService — site/LDAP/theme CRUD
│       ├── UserService.cs                   # IUserManagementService — user CRUD + role/perm grants
│       ├── RoleTemplateService.cs           # IRoleTemplateService — template CRUD + publish
│       ├── CurrentUserService.cs            # ICurrentUserService — JWT claim reader
│       ├── PasswordPolicy.cs                # IPasswordPolicy — 12-256 + composition
│       └── AuditRetentionService.cs         # BackgroundService — daily sweep (opt-in)
│
├── Syntera.DbSetup/        # .NET 10 console — DB create + migrate + seed
│   ├── Program.cs          # 3 steps: Platform DB → 6 site DBs → seed
│   └── Syntera.DbSetup.csproj   # references Syntera.Backend + copies its appsettings
│
└── Syntera.React/         # React 19 SPA
    ├── package.json
    ├── vite.config.ts            # port 5173, proxy /api → :5296
    ├── tsconfig*.json            # es2023, strict-ish, noUnused*
    ├── index.html                 # title "Syntera IAM"
    └── src/
        ├── App.tsx                # ThemeApplier + route table + menu builder
        ├── main.tsx               # initAuth() silent refresh + providers
        ├── index.css              # Tailwind v4 + 7 brand palettes (light + dark)
        ├── api/
        │   ├── client.ts          # axios instance + 401 refresh interceptor + single-flight
        │   ├── auth.ts            # login, logout, refresh, getProfile, initAuth
        │   ├── platform.ts        # sitesApi, roleTemplatesApi (system-admin, business-admin, ldap, theme)
        │   ├── site.ts            # usersApi (CRUD, roles, permissions)
        │   └── audit.ts          # auditApi.query
        ├── store/
        │   ├── authStore.ts       # Zustand: in-memory tokens + persisted theme (H7)
        │   └── themeStore.ts      # Zustand: light/dark preference
        ├── routes/
        │   └── guards.tsx         # RequireAuth, RequirePlatformAdmin,
        │                          # RequirePlatformOrSystemAdmin, RequireSiteAdmin, RequireRole
        ├── pages/
        │   ├── auth/LoginPage.tsx             # split-screen login
        │   ├── dashboard/DashboardPage.tsx     # adaptive role-based dashboard
        │   ├── platform/SitesPage.tsx          # 6 drawers (edit, LDAP, sysadmin, bizadmin, theme, ldap-test)
        │   ├── platform/RoleTemplatesPage.tsx # template CRUD + publish
        │   ├── site/UsersPage.tsx             # user CRUD + role/perm assignment
        │   ├── audit/AuditLogsPage.tsx         # filter + list
        │   └── settings/SettingsPage.tsx       # profile read-only + appearance + session info
        ├── components/
        │   ├── layout/
        │   │   ├── AdminLayout.tsx
        │   │   ├── AppHeader.tsx               # theme toggle + user dropdown
        │   │   ├── AppSidebar.tsx              # collapsible sidebar
        │   │   └── index.ts
        │   └── ui/
        │       ├── Avatar.tsx                  # @radix-ui/react-avatar wrapper
        │       ├── Button.tsx                  # 6 variants × 4 sizes
        │       ├── DropdownMenu.tsx             # @radix-ui/react-dropdown-menu wrapper
        │       └── index.ts
        ├── lib/cn.ts              # twMerge(clsx(...)) helper
        ├── types/index.ts         # mirror of backend DTOs (single file)
        └── assets/                # logo.jpg, logo-tagline.png, hero.png
```

---

## API Surface

All endpoints are JSON over HTTPS, returning a uniform `ApiResponse<T>`
envelope:

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
  "message": "Description of the error",
  "fieldErrors": [ { "field": "email", "message": "..." } ]
}
```

Error mapping (in `GlobalExceptionMiddleware`):

| Exception | HTTP | Default `errorCode` |
| --------- | ---- | ------------------- |
| `NotFoundException` | 404 | `NOT_FOUND` |
| `BusinessRuleException` | 409 | (caller-provided code) |
| `AuthenticationException` | 401 | (caller-provided code) |
| `AuthorizationException` | 403 | (caller-provided code) |
| Other `DomainException` | 400 | (caller-provided code) |
| Unhandled | 500 | `UNHANDLED` |

### Auth (anonymous)

| Method | Path | Description |
| ------ | ---- | ----------- |
| POST | `/api/auth/login` | Login by email + password (routes by domain) |
| POST | `/api/auth/refresh` | Refresh platform admin token (cookie-driven) |
| POST | `/api/auth/refresh-site` | Refresh site user token (body: `{ siteId }` + cookie) |

### Auth (authenticated)

| Method | Path | Description |
| ------ | ---- | ----------- |
| POST | `/api/auth/logout` | Revoke refresh token (cookie-driven) |
| GET | `/api/auth/profile` | Get current user profile from JWT |
| POST | `/api/auth/change-password` | Platform Admin: change own password |

### Platform Admin (`[PlatformAdminOnly]`)

| Method | Path | Description |
| ------ | ---- | ----------- |
| GET | `/api/platform/sites` | List all sites (`[PlatformAdminOrSystemAdmin]`) |
| GET | `/api/platform/sites/{id}` | Get a site (`[PlatformAdminOrSystemAdmin]`) |
| PUT | `/api/platform/sites/{id}` | Update a site (only `DisplayName` + `LdapDomains`) |
| POST | `/api/platform/sites/{siteId}/system-admin` | Assign System Admin (find-or-create) |
| GET | `/api/platform/sites/{siteId}/system-admins` | List System Admins |
| DELETE | `/api/platform/sites/{siteId}/system-admin/{userId}` | Revoke System Admin |
| GET | `/api/platform/sites/{siteId}/business-admins` | List Business Admins (manual System Admin scope check) |
| DELETE | `/api/platform/sites/{siteId}/business-admin/{userId}` | Revoke Business Admin (manual check) |
| GET | `/api/platform/sites/{siteId}/ldap-config` | Get LDAP config |
| PUT | `/api/platform/sites/{siteId}/ldap-config` | Upsert LDAP config |
| POST | `/api/platform/sites/ldap-test` | Test LDAP connection (test creds, audit) |
| GET | `/api/platform/sites/{siteId}/theme` | Get theme |
| PUT | `/api/platform/sites/{siteId}/theme` | Upsert theme |
| GET | `/api/platform/role-templates` | List role templates |
| GET | `/api/platform/role-templates/{id}` | Get a template |
| POST | `/api/platform/role-templates` | Create a template |
| PUT | `/api/platform/role-templates/{id}` | Update a template |
| POST | `/api/platform/role-templates/{id}/publish` | Publish → clone to all enabled sites |
| GET | `/api/platform/role-templates/permission-catalog` | List all permission keys |

> **Note on Business Admin endpoints:** `POST /api/platform/sites/{siteId}/business-admin`
> (assign) and the `business-admins` listing/revoke routes are gated by
> a **manual role check** in the controller: `_current.Roles.Contains("system-admin") && _current.SiteId == siteId`.
> Platform Admin bypasses this manual check for bootstrapping.

### Site Business Admin (`[HasPermission(...)]`)

| Method | Path | Permission | Description |
| ------ | ---- | ---------- | ----------- |
| GET | `/api/site/users` | `user.read` | List users in own site |
| GET | `/api/site/users/roles` | `role.read` | List roles (auto-clone from templates) |
| GET | `/api/site/users/{id}` | `user.read` | Get user with roles + direct perms |
| POST | `/api/site/users` | `user.write` | Create user (pre-provision) |
| PUT | `/api/site/users/{id}` | `user.write` | Update user (`DisplayName`, `Title`, `IsEnabled`) |
| POST | `/api/site/users/{id}/disable` | `user.disable` | Disable user |
| POST | `/api/site/users/assign-role` | `user_role.assign` | Assign role (with optional expiry) |
| POST | `/api/site/users/revoke-role` | `user_role.revoke` | Revoke role |
| POST | `/api/site/users/grant-permission` | `permission.grant` | Grant direct permission (≤90d, reason ≥10 chars) |
| POST | `/api/site/users/revoke-permission` | `permission.revoke` | Revoke direct permission |

### Audit (`[HasPermission("audit.read")]`)

| Method | Path | Description |
| ------ | ---- | ----------- |
| GET | `/api/audit/logs?from=&to=&action=&actorUserId=&outcome=&skip=&take=` | Query audit logs (Platform Admin → platform-wide; Site user → site-scoped) |

### Health

| Method | Path | Description |
| ------ | ---- | ----------- |
| GET | `/health` | Basic health check |

### Anti-self-edit protections

- `UserManagementService.UpdateAsync` refuses if `userId == currentUserId`
  (`SELF_EDIT_FORBIDDEN`).
- `DisableAsync`, `AssignRoleAsync`, `RevokeRoleAsync` refuse self-action.
- Only Platform Admin can assign/revoke the `site-business-admin` role
  (`INSUFFICIENT_PRIVILEGE`); System Admin assigns it via the
  `/api/platform/sites/{siteId}/business-admin` endpoint, not via
  `/api/site/users/assign-role`.

---

## Front-End Architecture

### Stack

- **React 19** + **TypeScript 6** (noUnusedLocals, noUnusedParameters,
  noFallthroughCasesInSwitch, erasableSyntaxOnly)
- **Vite 8** with **React Compiler** enabled (via `@rolldown/plugin-babel`
  + `babel-plugin-react-compiler`)
- **Tailwind CSS 4** (utility-first, via `@tailwindcss/vite`)
- **TanStack Query 5** (server state)
- **Zustand 4** (client state — `authStore` + `themeStore`)
- **React Router 7** (routing)
- **Axios** (HTTP client + 401-refresh interceptor)
- **Sonner** (toast notifications)
- **Lucide React** (icons)
- **Radix UI** (Avatar, DropdownMenu, Slot primitives — wrapped in-house,
  no external shadcn/ui or third-party UI lib at runtime)
- **oxlint** (linter)

### State Management

| Store | Persisted to localStorage | In-memory only |
| ----- | ------------------------ | ------------- |
| `authStore` | `theme` (the brand palette bundle) | `accessToken`, `refreshToken` (always null — cookie owns it), `expiresAt`, `profile`, `initializing` |
| `themeStore` | `isDark` (light/dark preference) | — |

**H7 security model (full — cookie-only transport, activated):**

- **Refresh token:** stored in **httpOnly cookie** `syntera_refresh`
  (set by backend, `Path=/api/auth`, `SameSite=Lax`, `Secure=!Dev`).
  JavaScript cannot read it; XSS cannot exfiltrate it. The backend runs in
  strict cookie-only mode (`Auth:CookieOnlyRefreshToken`, default `true`):
  the token never appears in a request or response JSON body, so even an
  XSS that can read fetch/XHR response bodies sees an empty string.
  `appsettings.Development.json` sets the flag to `false` so Swagger/.http
  and integration tooling can still use the body transport for debugging.
- **Access token:** in-memory only in `authStore`. On page reload, the
  store is empty; `initAuth()` runs a silent refresh from the cookie
  before React renders.
- **Profile:** re-fetched via silent refresh on app boot. Stale profile
  data after a role change would mislead the user — so we don't persist it.
- **Theme:** persisted to localStorage to avoid FOUC on the login page
  before silent refresh completes.

### Silent Refresh on Boot

`main.tsx` calls `await initAuth()` before `createRoot(...).render()`:

1. If `authStore.accessToken` already exists (HMR in dev, double-init),
   skip.
2. Otherwise POST `/api/auth/refresh` (no body — backend reads the cookie).
3. Success → populate `authStore` (accessToken, profile, theme). User
   appears logged in.
4. Failure → store stays empty. Login page renders.
5. Always `setInitializing(false)` in `finally`.

While `initializing === true`, all route guards render `<AuthInitializing />`
(full-screen spinner) instead of redirecting to `/login` — prevents the
"login page flash" for already-authenticated users.

### 401 Refresh Interceptor (single-flight)

In `client.ts`:

1. Request fails with 401 and is not on `/auth/*`.
2. Set `_retry=true` on the request config.
3. Call `acquireFreshAccessToken()`:
   - If a refresh is already in flight (`refreshPromise`), await it.
   - Otherwise POST `/api/auth/refresh` (platform) OR `/api/auth/refresh-site { siteId }` (site scope, determined from `profile.scope`).
4. On success → `setTokens(...)` + `updateTheme(...)`, retry the original request.
5. On failure → `logout()` + redirect to `/login`.

### Routing

```
/login                  — anonymous, public
/dashboard              — any authenticated user
/platform/sites         — Platform Admin OR System Admin
/platform/role-templates — Platform Admin only
/site/users             — Site Business Admin, Platform Admin, System Admin, eng-manager, supervisor, qo-manager
/audit/logs             — any authenticated user (Platform Admin sees all; Site user sees own site)
/site/audit             — alias for /audit/logs (site-scoped)
/settings               — any authenticated user
*                       — redirect to /dashboard
```

### Route Guards (`src/routes/guards.tsx`)

| Guard | Allowed roles |
| ----- | ------------- |
| `RequireAuth` | Any authenticated user |
| `RequirePlatformAdmin` | `platform-admin` |
| `RequirePlatformOrSystemAdmin` | `platform-admin` OR `system-admin` |
| `RequireSiteAdmin` | `platform-admin`, `site-business-admin`, `system-admin`, `eng-manager`, `supervisor`, `qo-manager` |
| `RequireRole({ roles })` | Generic, backward-compat |

All guards render `<AuthInitializing />` while `initializing=true`, redirect
to `/login` with `state.from` if unauthenticated, render `<ForbiddenPage />`
(403) if role not allowed.

### Menu Builder (`App.tsx → buildMenu`)

The sidebar menu is role-driven:

- **Dashboard** — always shown.
- **Platform Admin:** Sites, Role Templates, Audit Logs.
- **System Admin (not Platform):** Sites (to manage Business Admins for
  their own site).
- **Site Admin OR System Admin:** Users, Site Audit.
- **Settings** — always shown.

### Theme Application (`App.tsx → ThemeApplier`)

`<ThemeApplier />` runs at the App root (renders before `<Routes>`):

1. Reads `authStore.theme` and `themeStore.isDark`.
2. Picks `palette = isDark ? theme.dark : theme.light`.
3. Writes **two** sets of CSS variables to `document.documentElement`:
   - `--color-*` (10 vars): `primary`, `accent`, `background`, `surface`,
     `text`, `muted`, `border`, `success`, `warning`, `danger`. Used by
     inline-style pages.
   - Tailwind-style variables: `--background`, `--foreground`, `--card`,
     `--popover`, `--primary`, `--primary-foreground` (computed via
     `pickFg(hex)` luminance), `--accent`, `--muted`, `--border`, `--input`,
     `--ring`, `--secondary`, etc. Used by layout + UI components.
4. Sets `data-theme="<themeKey>"` on `<html>`.
5. Toggles `.dark` class on `<html>`.

### Pages

| Page | Route | Role-targeted | Notable features |
| ---- | ----- | ------------- | ---------------- |
| `LoginPage` | `/login` | Public | Split-screen brand panel + form; email/password; toggle show/hide |
| `DashboardPage` | `/dashboard` | All | Personalized welcome + 3 stat cards (roles, permissions, scope) + quick actions per role |
| `SitesPage` | `/platform/sites` | Platform Admin, System Admin | 6 slide-in drawers: SiteEdit, LDAP (with test login), SystemAdmin manage, BusinessAdmin manage, Theme, Manage admins list |
| `RoleTemplatesPage` | `/platform/role-templates` | Platform Admin | List + create/edit drawer with permission grid picker + Publish action |
| `UsersPage` | `/site/users` | Site Business Admin, Eng Manager, Supervisor, QO Manager, Platform Admin, System Admin | User CRUD + role assignment + direct permission grants (≤90d, with reason). Non-Platform cannot assign `site-business-admin` role. |
| `AuditLogsPage` | `/audit/logs` & `/site/audit` | All authenticated | Filter by action + outcome (success/failure) + list |
| `SettingsPage` | `/settings` | All authenticated | Profile (read-only) + appearance (light/dark toggle) + session info |

### Folder Map (Frontend)

| Path | Purpose |
| ---- | ------- |
| `src/api/` | Single axios instance + per-aggregate endpoint helpers |
| `src/components/ui/` | In-house Radix wrappers (Avatar, DropdownMenu, Button) |
| `src/components/layout/` | Admin shell (AdminLayout, AppSidebar, AppHeader) |
| `src/lib/` | `cn()` class composer (twMerge + clsx) |
| `src/pages/` | One folder per domain (auth, dashboard, platform, site, audit, settings) |
| `src/routes/` | 5 route guards |
| `src/store/` | Zustand stores (`authStore`, `themeStore`) |
| `src/types/` | Mirror of backend DTOs (single file) |
| `src/index.css` | Tailwind v4 entry + 7 brand palettes (light + dark) + animations |

### What's NOT in the Frontend

- **No `AppBreadcrumb`** component (sidebar handles navigation feedback).
- **No `DataTable`, `Modal`, `Field`** components — pages use native
  `<table>` and custom in-page drawers (`<Drawer>` helper in `SitesPage.tsx`).
- **No `src/providers/`** folder — axios reads directly from Zustand, no
  context provider needed.
- **No `src/hooks/`** folder — mutation hooks are inlined into pages
  (intentional for visibility).
- **No `VITE_API_BASE_URL` env var** — `client.ts` hardcodes `baseURL: "/api"`.
  Dev relies on Vite proxy (`/api` → `:5296`); production relies on a
  reverse proxy mapping `/api/*` → backend.
- **No `change-password` UI** — `SettingsPage` is read-only. (Backend
  endpoint `POST /api/auth/change-password` exists for Platform Admin via
  `curl` or Swagger.)
- **No LDAP sync UI** — manual user provisioning only (per comment in
  `api/site.ts`: "LDAP sync is NOT available (no service account). Business
  Admin must create user rows manually before users can log in.")

### Conventions

- **One Axios instance.** Never call `fetch` or create a new `axios.create()`
  — go through `src/api/client.ts` so JWT refresh and envelope unwrap run
  consistently.
- **Typed wrappers.** Use `get<T>`, `post<T>`, `put<T>`, `patch<T>`,
  `del<T>` from `src/api/client.ts` — they return `Promise<T>` (unwrapped).
- **UI primitives are owned in-house.** When you need a new Radix
  primitive, add it under `src/components/ui/` following the Avatar /
  DropdownMenu pattern (`forwardRef` + `cn(...)` + brand classes). Do NOT
  pull in an external shadcn/ui or third-party UI package.
- **TanStack Query.** Read endpoints → `useQuery`; mutations → `useMutation`
  + `queryClient.invalidateQueries(...)` on success. `retry`: no retry for
  4xx, max 3 retries for 5xx / network. `staleTime`: 60s.
  `refetchOnWindowFocus: false`.
- **Branding.** Always reference `var(--primary)`, `var(--accent)`, etc.
  — never raw hex. Brand palette lives in `src/index.css` (7 themes).
- **Accessibility.** Every button has an `aria-label` when its content is
  icon-only. Every form `<label>` wraps its input.

---

## Security Model

### Authentication

1. **Platform Admin** — Local bcrypt credential (work factor 12) in
   `PlatformUsers` table. Must work even if all site LDAPs are down.
2. **Site Users** — **LDAP direct bind** with user's own email + password.
   LDAP server must use LDAPS (port 636) or StartTLS (port 389). Plain
   389 is rejected at save time (unless `Ldap:AllowPlainPort389=true` in
   dev). The backend does **not** store or use a service-account bind DN;
   `SiteLdapConfig` carries only `Host`, `Port`, `UseStartTls`, `BaseDn`,
   and optional `UpnDomain` (for `user@domain` → bind-DN transformation).
3. **JWT** — HS256 signed, 15-minute lifetime (Dev: 60 min), carries:
   - `sub` (user ID), `email`, `display_name`, optional `title`
   - `scope` (`platform` | `site`)
   - `site_id`, `site_code` (null for platform)
   - `perm_ver` (for stale-perm detection)
   - `role[]` claims (one `ClaimTypes.Role` per role)
   - `perm[]` claims (one per effective permission)
   - `is_platform_admin=true` (if role `platform-admin`)
   - `is_site_admin=true` (if role `site-business-admin`)
   - Issuer `syntera`, Audience `syntera-api`, clock skew 30s

### Refresh Token

- **Format:** `{base64url(32-byte random)}.{base64url(HMAC-SHA256(random))}`.
  The HMAC key is derived from `Jwt:SigningKey` via
  `SHA256("syntera:refresh-hmac:v1:" + jwtKey)`. Signature verification
  uses `CryptographicOperations.FixedTimeEquals` (constant-time) and runs
  **before** any DB lookup (DoS mitigation against invalid tokens).
- **Storage:** Opaque random part, stored as SHA-256 hash in DB (`TokenHash`,
  unique index). Platform scope → `PlatformDbContext.RefreshTokens`;
  site scope → `SiteDbContext.RefreshTokens` in the site DB. Sent to client
  via **httpOnly cookie** `syntera_refresh` (`Path=/api/auth`, `SameSite=Lax`,
  `Secure=!Dev`).
- **TTL per scope:** platform default 1 day, site default 7 days
  (`Jwt:RefreshTokenDaysPlatform` / `Jwt:RefreshTokenDaysSite`,
  fallback `Jwt:RefreshTokenDays`).
- **Rotation:** On every refresh, the old token is revoked
  (`RevokedAt`, `ReplacedById`) and a new one is issued with the **same
  `FamilyId`** and `ReplacedById` pointing to the parent.
- **Reuse detection (M1):** If a token with `RevokedAt != null` or
  `ReplacedById != null` is used again, the entire family is revoked
  (`RevokeFamilyAsync`) and the request throws `REFRESH_REUSE_DETECTED`.
- **Revocable:** Logout (`POST /api/auth/logout`) revokes the token from
  the cookie. Admin force-revoke is server-side only.
- **Tracked server-side per user** — stolen tokens can be invalidated.

### Authorization

- **Fail-closed** — Anonymous requests are denied by default.
- **Permission-based** — `[HasPermission("user.write")]` checks JWT
  `perm` claim.
- **Platform Admin bypass** — Platform admin tokens (`is_platform_admin=true`)
  bypass all `[HasPermission]` checks.
- **Tenant isolation** — Site Business Admins can NEVER touch users in
  another site (enforced by `SiteDbContextFactory` resolving the
  connection string from JWT `site_id` claim). `ResolveAsync()` without
  a site_id claim throws `InvalidOperationException` (fail-closed).

### LDAP Credential Storage

`SiteLdapConfig` (which stores `Host`, `Port`, `BaseDn`, `UpnDomain`)
does **not** store a bind password — the backend uses **direct bind**
with each user's own credentials. There is no service account to protect.

> **Note:** `Site.DatabaseConnectionString` has a code comment "Stored
> encrypted via DPAPI" but **no DPAPI encryption is implemented** — the
> connection string is stored as plaintext in the platform DB. (See
> Operational Runbook for backup & access-control guidance.)

### Password Policy (Platform Admin only)

`PasswordPolicy` (configured via `PasswordPolicy:*` keys):

- MinLength 12, MaxLength 256
- RequireUpper, RequireLower, RequireDigit, RequireSymbol = true
- Enforced in `AuthService.ChangePasswordAsync`. All violations are
  collected and joined with `"; "` → `PASSWORD_POLICY_VIOLATION` business
  rule error.
- Refuses no-op rotation (`PASSWORD_SAME_AS_CURRENT`).
- Site users authenticate via LDAP — their password policy is AD's, not ours.

### Rate Limiting

- **Default policy** — 500 requests/min per IP.
- **Auth policy** — 20 requests/min per IP for `/api/auth/*` endpoints.
- **User-level lockout** — After 5 failed logins per IP+email AND per email
  (2-bucket), the account is locked for 15 minutes (`LockedUntil` field
  on `PlatformUser` or `User`). On successful login, both buckets are
  cleared.

### CORS

- **Fail-closed in Production** — If `Cors:AllowedOrigins` is empty,
  all cross-origin requests are rejected (`SetIsOriginAllowed(_ => false)`).
- **DevOrigins** — A separate list for development-only origins
  (`http://localhost:5173`, `http://localhost:4173`), ignored in Production.
- Dev empty list → allow `http://localhost*`.

### HTTPS / HSTS

- `UseHttpsRedirection` + `UseHsts` enabled in Production.
- `RequireHttpsMetadata = true` for JWT validation in Production.

### Security Headers (Middleware)

`SecurityHeadersMiddleware` sets:

- `Content-Security-Policy` — strict in Prod (`default-src 'self'`,
  `script-src 'self'`, `style-src 'self' 'unsafe-inline'`,
  `img-src 'self' data: blob:`, `frame-ancestors 'none'`, `base-uri 'self'`,
  `form-action 'self'`, `object-src 'none'`). Relaxed in Dev for Vite HMR
  + React Refresh + `ws://localhost:5173/4173`.
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Permissions-Policy: camera=(), microphone=(), geolocation=(),
  interest-cohort=(), browsing-topics=(), payment=(), usb=(), serial=(),
  bluetooth=()`

HSTS is handled by `UseHsts()` in Production (not duplicated in the
middleware).

---

## Audit & Compliance

### Audit Log Properties

- **Append-only** — UPDATE and DELETE rejected at the EF Core `SaveChanges`
  pipeline level (`RejectAuditLogMutation` in `PlatformDbContext` and
  `SiteDbContext`). Throws if any `AuditLog` entity is in state `Modified`
  or `Deleted`.
- **Hash-chained (M3)** — Each entry's `Hash` is `SHA-256` of
  `PreviousHash + Timestamp(O) + SiteId + ActorUserId + ActorEmail + Action
  + TargetType + TargetId + Outcome + ErrorMessage + AfterJson`. The
  `AfterJson` field is **required** in the hash payload to prevent
  tampering with the recorded post-state. Any retroactive tampering is
  detectable by recomputing the chain.
- **Denormalized actor info** — `ActorEmail`, `ActorIp`, `ActorUserAgent`
  are stored on each entry so forensic queries work even after user
  deletion.
- **Dual scope** — Platform-level actions go to `syntera_master.AuditLogs`;
  site-level actions go to the site's `AuditLogs` table.
- **Never throws** — `AuditService.LogAsync` swallows write failures and
  logs them via `[LoggerMessage] LogAuditWriteFailure`. The audit failure
  does NOT abort the business operation (trade-off: keep the platform
  running over blocking on audit).

### Retention

- **Default:** 10 years (configurable via `Audit:RetentionYears`).
- **Sweeper:** `AuditRetentionService` is a `BackgroundService` that runs
  every **24 hours** (hardcoded interval, 30-second startup delay).
- **Opt-in:** Only runs if `Audit:EnforceRetention=true`. Disabled by
  default because regulated environments often keep audit logs forever
  and archive to cold storage separately.
- **Implementation:** Computes cutoff = `UtcNow.AddYears(-retentionYears)`,
  then runs **raw SQL** `DELETE FROM AuditLogs WHERE Timestamp < @cutoff`
  (bypasses the `RejectAuditLogMutation` guard via raw SQL — intentional).
  Applied to Platform DB + all enabled site DBs.

### Audit Events Captured

| Event | Trigger |
| ----- | ------- |
| `auth.login` | Login attempt (success or failure) |
| `auth.account_locked` | Platform Admin locked after 5 failed attempts |
| `auth.password_change` | Platform Admin changed own password |
| `site.create` / `site.update` / `site.disable` | Platform admin site actions |
| `ldap.write` / `ldap.test_connection` | LDAP config changes (test result audited without password) |
| `theme.write` | Theme palette changes |
| `role_template.create` / `role_template.publish` | Role template lifecycle |
| `system_admin.assign` / `system_admin.revoke` | System Admin delegation |
| `business_admin.assign` / `business_admin.revoke` | Business Admin delegation |
| `user.create` / `user.update` / `user.disable` | Site user lifecycle |
| `user_role.assign` / `user_role.revoke` | Role assignment changes |
| `permission.grant` / `permission.revoke` | Direct permission changes |

> **Note:** `role_template.update` is NOT audited — the `UpdateAsync` in
> `RoleTemplateService` uses raw SQL inside a transaction to work around
> EF change-tracker concurrency issues, and the audit call was not added
> in that path.

---

## Brand Theming

Each site has a `SiteTheme` record in the platform DB:

```json
{
  "themeKey": "kalventis-default",
  "lightPaletteJson": "{\"primary\":\"#007A4D\",\"accent\":\"#00A7B5\",...}",
  "darkPaletteJson":  "{\"primary\":\"#34D399\",\"accent\":\"#22D3EE\",...}",
  "logoUrl": null
}
```

### Seeded Themes (6 sites + 1 platform default)

| Site | Light Primary | Light Accent | Dark Primary | Dark Accent |
| ---- | ------------- | ----------- | ------------ | ----------- |
| Syntera (default, `syntera-default`) | `#0B3D6F` navy | `#00A7B5` teal | `#60A5FA` | `#22D3EE` |
| Kalventis | `#007A4D` emerald | `#00A7B5` | `#34D399` | `#22D3EE` |
| Kalbe | `#E2231A` crimson | `#00A7B5` | `#F87171` | `#22D3EE` |
| Dankos | `#0054A6` royal blue | `#00A7B5` | `#60A5FA` | `#22D3EE` |
| Hexpharm | `#00796B` teal | `#00A7B5` | `#2DD4BF` | `#22D3EE` |
| Fima | `#6B46C1` violet | `#00A7B5` | `#A78BFA` | `#22D3EE` |
| GOF | `#C2410C` amber | `#00A7B5` | `#FB923C` | `#22D3EE` |

All 7 themes (light + dark) are defined as CSS variables in
`Syntera.React/src/index.css` under `:root[data-theme="<name>"]` and
`:root[data-theme="<name>"].dark`. The frontend's `<ThemeApplier />`
overrides these at runtime via `document.documentElement.style.setProperty`.

### Performance

- Themes are cached in-memory for 5 minutes (`IMemoryCache` key
  `theme:{siteId}`).
- Cache invalidated on theme update via `IThemeService.InvalidateCacheAsync`.
- No per-request DB hit on the hot path (login → theme → response).

### User Override

Users can toggle between light/dark mode via the header toggle. The
preference is stored in `localStorage` (`syntera.theme`, key `isDark`).
Initial value comes from `window.matchMedia("(prefers-color-scheme: dark)")`.
The brand palette is NOT user-overridable — it is determined by the user's
site.

---

## Operational Runbook

### Adding a New Site

> **Note:** The current build ships **6 fixed sites** seeded from
> `appsettings.json` `Sites[]`. The seeder (`DbSeeder.EnsureSitesAsync`)
> is idempotent — existing sites are updated (DisplayName, theme,
> connection string) but new sites cannot be created at runtime via the
> API (the `POST /api/platform/sites` endpoint mentioned in earlier
> docs is **not implemented** in the current build). To add a new site:

1. Add an entry to `Sites[]` in `appsettings.json` (Code, DisplayName,
   EmailDomain, LightPalette, DarkPalette).
2. Add a connection string to `ConnectionStrings:Sites:{code}` in
   `appsettings.json` and user-secrets.
3. Run `./setup-db.sh` (or `cd Syntera.DbSetup && dotnet run`). The new
   DB is created and migrated; the site row is inserted; the theme is
   upserted; the LDAP domain is registered.
4. Restart the Backend.

### Delegating a System Admin

1. **Platform Admin** logs in to the UI → Sites → pick a site →
   **System Admins** → **Assign System Admin**.
2. Enter the user's LDAP email + display name. Backend finds or creates
   the user in the site DB (BCrypt-free — they will authenticate via LDAP),
   auto-clones the `system-admin` role + 7 other published templates into
   the site DB, and assigns the role.
3. The user can now log in via LDAP and manage Business Admins for their
   site (via the same Sites page).

### Delegating a Site Business Admin

1. **System Admin** (or Platform Admin for bootstrapping) logs in →
   Sites → pick own site → **Business Admins** → **Assign Business Admin**.
2. Enter the user's LDAP email + display name. Backend find-or-create +
   auto-clone + assign `site-business-admin` role.
3. The user can now log in via LDAP and manage users in their site.

> Note: Platform Admin can also assign Business Admins (manual check
> in the controller bypasses the System Admin scope check for bootstrapping).

### Triggering Role Template Publish

Platform Admin → **Role Templates** → **Publish** on a draft/published
template:

1. Set `IsPublished=true`, `Version++`.
2. Clone to ALL enabled sites via `CloneTemplateToSiteAsync`:
   - Resolve site DB via `ResolveForSiteAsync(site.Id)` (NOT via JWT claim).
   - Find-or-update `Role` by `Key`.
   - Insert missing `Permission` rows.
   - Replace `RolePermissions` via raw SQL (delete + insert).
   - Bump `PermissionsVersion` for all users in that site who have the
     role.
3. Audit `role_template.publish`.
4. Partial failures (one site fails to clone) →
   `BusinessRuleException("PUBLISH_PARTIAL_FAILURE")` with error list.

### Recovering from LDAP Outage

If a site's LDAP is down:

- All users from that site cannot log in (no fallback, per requirement).
- **Platform Admin** can still log in (`admin@syntera.com` uses local
  bcrypt credential).
- Platform Admin can disable the site (`PUT /api/platform/sites/{id}`
  with `IsEnabled=false` is NOT currently exposed — but the manual SQL
  `UPDATE Sites SET IsEnabled=0 WHERE Code = '...'` works) to prevent
  confusion, then re-enable when LDAP is back.

### Rotating the JWT Signing Key

1. Generate a new key (min 32 chars, random):
   `openssl rand -base64 48 | tr -d '/+=' | head -c 48`.
2. Set via environment variable:
   `SYNTERA_Jwt__SigningKey=<new-key>`.
3. Restart the API.
4. All existing JWTs become invalid — users will be force-logged-out
   (access tokens rejected). Their refresh tokens still work — the
   refresh flow verifies the HMAC with the **current** signing key
   (which also changed), so refresh-cookie rotation also fails. Users
   will need to log in fresh.

### Backup & Restore

- **Platform DB** — Back up daily. Contains all site configs, role
  templates, platform admin credentials, and platform audit logs.
- **Site DBs** — Back up independently per site. Restore one site
  without affecting others.
- **Site `DatabaseConnectionString`** — stored as **plaintext** in
  `syntera_master.Sites`. Restrict access to the platform DB at the
  SQL Server level (separate SQL login for the API; no direct human
  access to the master DB).
- **DPAPI key ring** — `DataProtection:KeyPath` is configured but
  currently unused (no DPAPI-encrypted fields in the schema). Safe to
  skip backup of the key directory until encryption is added.

### Daily Commands

```bash
# Start dev (backend :5296 + frontend :5173)
./dev.sh

# Database setup (idempotent — safe after every git pull)
./setup-db.sh

# Comprehensive diagnostics
./diagnose.sh

# Add a new Platform migration
cd Syntera.Backend
dotnet ef migrations add <Name> --context PlatformDbContext --output-dir Data/Migrations/Platform
dotnet ef database update --context PlatformDbContext

# Add a new Site migration
cd Syntera.Backend
dotnet ef migrations add <Name> --context SiteDbContext --output-dir Data/Migrations/Site
# Apply to all 6 site DBs via:
cd ../Syntera.DbSetup && dotnet run

# Build production
cd Syntera.Backend && dotnet publish -c Release -o ./publish
cd Syntera.React && bun run build
```

### Default Credentials (Development)

| Email | Password | Scope |
|-------|----------|-------|
| `admin@syntera.com` | (from `Seed:PlatformAdminPassword`, default `ChangeMe!Strong#1`) | Platform Admin |

**Change this before production** via:
```bash
export SYNTERA_Seed__PlatformAdminPassword="YourStrongProductionPa55!"
```
