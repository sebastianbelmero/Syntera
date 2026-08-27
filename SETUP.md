# Syntera IAM — Setup Guide

Panduan setup dari awal sampai aplikasi bisa dijalankan dan login berhasil.

## Prerequisites

| Tool | Version | Cara Cek |
|------|---------|----------|
| .NET SDK | 10.0+ | `dotnet --version` |
| Node.js atau Bun | 24+ / 1.3+ | `node --version` atau `bun --version` |
| SQL Server | 2022 (local / remote / Docker) | `sqlcmd -Q "SELECT @@VERSION"` |

---

## Step 1: Setup SQL Server

### Opsi A: SQL Server di Docker (paling gampang)

```bash
docker run -d \
  --name syntera-sql \
  -e "ACCEPT_EULA=Y" \
  -e "MSSQL_SA_PASSWORD=YourStrongPass123!" \
  -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest
```

Verifikasi:
```bash
docker exec -it syntera-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "YourStrongPass123!" -C -Q "SELECT 1"
```

### Opsi B: SQL Server lokal (sudah terinstall)

Pastikan SQL Server Browser service running, dan TCP/IP protocol enabled
(di SQL Server Configuration Manager).

### Opsi C: SQL Server remote

Pastikan firewall mengizinkan koneksi ke port 1433, dan SQL Server
menerima koneksi dari IP Anda.

---

## Step 2: Konfigurasi Backend Secrets

```bash
cd Syntera.Api

# Inisialisasi user-secrets (Development environment)
dotnet user-secrets init

# Set connection string — GANTI password sesuai SQL Server Anda
dotnet user-secrets set "ConnectionStrings:Platform" \
  "Server=localhost,1433;Database=syntera_master;User Id=sa;Password=YourStrongPass123!;TrustServerCertificate=True;MultipleActiveResultSets=True"

# Set JWT signing key (min 32 karakter — generate random)
dotnet user-secrets set "Jwt:SigningKey" \
  "$(openssl rand -base64 48 | tr -d '/+=' | head -c 48)"

# Set Platform Admin password (admin@syntera.com)
dotnet user-secrets set "Seed:PlatformAdminEmail" "admin@syntera.com"
dotnet user-secrets set "Seed:PlatformAdminPassword" "ChangeMe!Strong#1"
```

Verifikasi:
```bash
dotnet user-secrets list
```

Harusnya muncul semua key di atas.

---

## Step 3: Install EF Core Tool

Cek apakah sudah terinstall:
```bash
dotnet ef --version
```

Kalau belum:
```bash
dotnet tool install --global dotnet-ef --version 10.0.11
```

Pastikan `~/.dotnet/tools` ada di PATH:
```bash
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc
source ~/.bashrc
```

---

## Step 4: Apply Database Migrations

Ada **2 DbContext** — masing-masing punya migration sendiri:

```bash
cd Syntera.Api

# Migrate Platform DB (master)
dotnet ef database update --context PlatformDbContext

# (Optional) Generate SQL script untuk review
dotnet ef migrations script --context PlatformDbContext --output ../migrate-platform.sql
```

Output yang benar:
```
Build started...
Build succeeded.
Applying migration '20260827043056_InitialPlatform'.
Done.
```

Verifikasi tabel terbuat:
```bash
# Kalau pakai Docker
docker exec -it syntera-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "YourStrongPass123!" -C -Q "
USE syntera_master;
SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_NAME;
"
```

Harusnya muncul tabel-tabel: `Sites`, `SiteLdapDomains`, `SiteLdapConfigs`,
`SiteThemes`, `RoleTemplates`, `RoleTemplatePermissions`, `PlatformUsers`,
`RefreshTokens`, `AuditLogs`, `PlatformSettings`, `__EFMigrationsHistory_Platform`.

---

## Step 5: Seed Default Data (otomatis saat run)

Seeder berjalan otomatis saat `dotnet run` di Development mode.
Seeder akan membuat:
- Default platform settings (audit retention 10 tahun, dll.)
- 2 role templates: `viewer`, `site-business-admin`
- Platform Admin user: `admin@syntera.com` (password dari user-secrets)

**Tidak perlu manual SQL** — seeder idempotent dan aman dijalankan berulang.

---

## Step 6: Jalankan Backend

```bash
cd Syntera.Api
export ASPNETCORE_ENVIRONMENT=Development
dotnet run
```

Output yang benar:
```
[11:20:12 INF] Now listening on: http://localhost:5000
[11:20:12 INF] Application started. Press Ctrl+C to shut down.
```

Swagger UI: http://localhost:5000/docs

---

## Step 7: Test Login Platform Admin

Dari terminal lain:

```bash
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@syntera.com",
    "password": "ChangeMe!Strong#1"
  }'
```

Output yang benar (HTTP 200):
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJ...",
    "expiresAt": "2026-08-27T11:35:12Z",
    "refreshToken": "abc123...",
    "profile": {
      "userId": "...",
      "email": "admin@syntera.com",
      "displayName": "Platform Admin",
      "scope": "platform",
      "roles": ["platform-admin"],
      "permissions": ["site.create", "site.read", ...]
    },
    "theme": {
      "themeKey": "syntera-default",
      "light": { "primary": "#0B3D6F", ... },
      "dark": { "primary": "#60A5FA", ... }
    }
  }
}
```

Kalau dapat **401**: cek password, atau cek tabel `PlatformUsers` di DB.

---

## Step 8: Jalankan Frontend

```bash
cd Syntera.React
bun install   # atau: npm install
bun run dev   # atau: npm run dev
```

Buka: http://localhost:5173

Login dengan:
- Email: `admin@syntera.com`
- Password: `ChangeMe!Strong#1`

---

## Step 9: Setup Site Database (untuk user LDAP)

Setelah login sebagai Platform Admin, buat site pertama Anda:

1. Buka UI → **Sites** → **New Site**
2. Isi:
   - Code: `kalventis`
   - Display Name: `PT Kalventis Surya Pratama`
   - Database Connection String: `Server=localhost,1433;Database=syntera_kalventis;User Id=sa;Password=YourStrongPass123!;TrustServerCertificate=True;MultipleActiveResultSets=True`
   - Email Domains: `kalventis.com`
   - Default Theme Key: `kalventis-navy`
3. Save

**Buat database kosong** untuk site tersebut:
```bash
docker exec -it syntera-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "YourStrongPass123!" -C -Q "CREATE DATABASE syntera_kalventis;"
```

**Apply migration Site** ke database site tersebut:
```bash
cd Syntera.Api

# Set connection string site sementara
export SYNTERA_ConnectionStrings__Site="Server=localhost,1433;Database=syntera_kalventis;User Id=sa;Password=YourStrongPass123!;TrustServerCertificate=True;MultipleActiveResultSets=True"

# Apply migration
dotnet ef database update --context SiteDbContext --connection "$SYNTERA_ConnectionStrings__Site"
```

Ulangi untuk setiap site (kalbe, dankos, hexpharm, fima, gof).

---

## Step 10: Konfigurasi LDAP per Site

Di UI → **Sites** → klik **LDAP** pada site yang sesuai:

- Host: `10.131.220.11`
- Port: `636` (LDAPS) atau `389` + StartTLS
- Base DN: `DC=KALVENTIS,DC=DOM`
- Email Attribute: `userPrincipalName`
- Bind DN: (service account, contoh: `CN=svc-syntera,OU=ServiceAccounts,DC=KALVENTIS,DC=DOM`)
- Bind Password: (password service account)
- User Filter Template: `(&(objectClass=user)({emailAttribute}={email}))`

Klik **Test** dengan email user LDAP yang valid. Jika berhasil, klik **Save**.

---

## Step 11: Provision Site Users

**Opsi A: Trigger LDAP Sync** (otomatis, butuh bind credential)

1. Login sebagai `admin@syntera.com` di UI
2. Buka site Kalventis → assign role `site-business-admin` ke user dari LDAP sync
3. Atau: login sebagai site-business-admin → buka **Users** → **Sync LDAP**

**Opsi B: Manual provisioning** (untuk testing tanpa LDAP)

```bash
# Login sebagai platform admin, dapatkan token
TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@syntera.com","password":"ChangeMe!Strong#1"}' \
  | jq -r '.data.accessToken')

# Tapi ini butuh site token, jadi harus login via LDAP dulu.
# Untuk dev/testing tanpa LDAP, gunakan SQL insert langsung:
```

```sql
-- Di syntera_kalventis database
USE syntera_kalventis;

-- Pastikan role 'site-business-admin' sudah di-clone dari template
-- (trigger via Platform Admin: publish role template → auto-clone)

-- Insert user manual
INSERT INTO Users (Id, Email, DisplayName, IsEnabled, SiteId, PermissionsVersion, CreatedAt, UpdatedAt)
VALUES (NEWID(), 'test.user@kalventis.com', 'Test User', 1,
        (SELECT Id FROM (SELECT NEWID() AS Id) AS tmp), 1, SYSUTCDATETIME(), SYSUTCDATETIME());
```

---

## Troubleshooting

### "More than one DbContext was found"

Solusi: selalu specify `--context`:
```bash
dotnet ef migrations add <name> --context PlatformDbContext --output-dir Migrations/Platform
dotnet ef database update --context PlatformDbContext
```

### Login returns 401 "Invalid credentials"

Kemungkinan:
1. **User belum di-seed** — jalankan `dotnet run` di Development mode, seeder akan create admin.
2. **Password salah** — cek `dotnet user-secrets list`, pastikan `Seed:PlatformAdminPassword` benar.
3. **DB belum di-migrate** — jalankan `dotnet ef database update --context PlatformDbContext`.
4. **User disabled** — cek di tabel `PlatformUsers`, kolom `IsEnabled = 1`.

Cek password hash di DB:
```sql
USE syntera_master;
SELECT Email, PasswordHash, IsEnabled FROM PlatformUsers;
```

### "Connection string not found"

```bash
dotnet user-secrets list
```

Pastikan `ConnectionStrings:Platform` ada. Kalau tidak, set ulang (lihat Step 2).

### Lint warnings di frontend

```bash
cd Syntera.React
bun run lint
```

Harusnya **0 warnings, 0 errors**. Kalau masih ada, jalankan:
```bash
bun run build  # build akan fail kalau ada type error
```

### Frontend build error "onSuccess does not exist"

TanStack Query v5 tidak mendukung `onSuccess`/`onError` di `useQuery`.
Gunakan error handling di dalam `queryFn` atau pakai `useMutation` untuk writes.

### LDAP test gagal

1. Pastikan port 636 (LDAPS) atau 389+StartTLS — **port 389 tanpa StartTLS ditolak**.
2. Test dengan `ldapsearch` dari command line dulu:
   ```bash
   ldapsearch -H ldaps://10.131.220.11:636 -D "bind_dn" -W -b "DC=KALVENTIS,DC=DOM" "(userPrincipalName=test@kalventis.com)"
   ```
3. Kalau SSL cert self-signed, dev mode akan accept (lihat `NovellLdapClient.cs`).

---

## Quick Reference: Perintah Sehari-hari

```bash
# Backend dev
cd Syntera.Api
export ASPNETCORE_ENVIRONMENT=Development
dotnet run

# Frontend dev
cd Syntera.React
bun run dev

# Lint
cd Syntera.React && bun run lint

# Typecheck
cd Syntera.React && bun run typecheck

# Build production
cd Syntera.Api && dotnet publish -c Release -o ./publish
cd Syntera.React && bun run build

# Migration baru (kalau ada perubahan entity)
cd Syntera.Api
dotnet ef migrations add <Name> --context PlatformDbContext --output-dir Migrations/Platform
dotnet ef migrations add <Name> --context SiteDbContext --output-dir Migrations/Site

# Apply migration
dotnet ef database update --context PlatformDbContext
dotnet ef database update --context SiteDbContext --connection "<site-conn-string>"

# Reset DB (hati-hati!)
dotnet ef database drop --context PlatformDbContext --force
```

### Catatan: database lama era `EnsureCreated` (baseline otomatis)

Build IAM awal membuat `syntera_master` lewat `EnsureCreated` — skema lengkap
TANPA migration history, sehingga `MigrateAsync` biasa akan crash
(`There is already an object named 'AuditLogs' in the database`).
Sekarang `dotnet run` (Development) mendeteksi kondisi ini lewat
`DatabaseInitializer` dan otomatis **baseline**: semua migration tercatat sebagai
sudah diterapkan tanpa replay, data lama (admin, sites, themes, audit) tetap utuh.
Instalasi baru dan penambahan migration berikutnya tetap lewat jalur normal.

---

## Default Credentials (Development)

| Email | Password | Scope |
|-------|----------|-------|
| admin@syntera.com | (set via user-secrets, default: `ChangeMe!Strong#1`) | Platform Admin |

**Ganti password ini sebelum production!** Set via environment variable:
```bash
export SYNTERA_Seed__PlatformAdminPassword="YourStrongProductionPa55!"
```
