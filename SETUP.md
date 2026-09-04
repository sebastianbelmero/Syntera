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
cd Syntera.Backend

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

## Step 3: Create All Databases & Apply Migrations (One Command)

Syntera punya **2 DbContext** (Platform + Site) dan **7 database** (1 master + 6 site).
Untungnya, ada `Syntera.DbSetup` — console project yang otomatis:

1. Create 7 database jika belum ada (`syntera_master` + `syntera_kalventis`, `syntera_kalbe`, `syntera_fima`, `syntera_gof`, `syntera_dankos`, `syntera_hexpharm`)
2. Apply PlatformDbContext migration ke `syntera_master`
3. Apply SiteDbContext migration ke 6 site database
4. Seed platform data (admin user, role templates, 6 sites + themes)

### Cara pakai

```bash
cd ~/Syntera

# Opsi A: pakai wrapper script
./setup-db.sh

# Opsi B: langsung pakai dotnet
cd Syntera.DbSetup
dotnet run
```

### Output yang benar

```
[14:35:01 INF] ════════════════════════════════════════════════════════════════
[14:35:01 INF]   Syntera DbSetup — creating all databases & applying migrations
[14:35:01 INF] ════════════════════════════════════════════════════════════════
[14:35:01 INF]
[14:35:01 INF] ▶ Step 1/3: Platform database (syntera_master)
[14:35:01 INF]
[14:35:01 INF]   Ensuring database 'syntera_master' exists...
[14:35:02 INF]   ✓ Database 'syntera_master' ready
[14:35:02 INF]   Applying PlatformDbContext migrations...
[14:35:03 INF]   ✓ Platform migrations applied
[14:35:03 INF]
[14:35:03 INF] ▶ Step 2/3: Site databases (6 sites)
[14:35:03 INF]
[14:35:03 INF]   [KALVENTIS]
[14:35:03 INF]   Ensuring database 'syntera_kalventis' exists...
[14:35:04 INF]   ✓ Database 'syntera_kalventis' ready
[14:35:04 INF]     Applying SiteDbContext migrations...
[14:35:04 INF]     ✓ kalventis migrations applied
[14:35:04 INF]   [KALBE]
[14:35:04 INF]   ... (repeat for fima, gof, dankos, hexpharm)
[14:35:15 INF]
[14:35:15 INF] ▶ Step 3/3: Seed platform data (admin user, role templates, 6 sites, themes)
[14:35:15 INF]
[14:35:15 INF]   ✓ Seeding complete
[14:35:15 INF]
[14:35:15 INF] ════════════════════════════════════════════════════════════════
[14:35:15 INF]   ✓ All databases ready!
[14:35:15 INF]
[14:35:15 INF]   Platform DB: syntera_master (11 tables)
[14:35:15 INF]   Site DB kalventis : syntera_kalventis (9 tables)
[14:35:15 INF]   Site DB kalbe     : syntera_kalbe (9 tables)
[14:35:15 INF]   Site DB fima      : syntera_fima (9 tables)
[14:35:15 INF]   Site DB gof       : syntera_gof (9 tables)
[14:35:15 INF]   Site DB dankos    : syntera_dankos (9 tables)
[14:35:15 INF]   Site DB hexpharm  : syntera_hexpharm (9 tables)
[14:35:15 INF]
[14:35:15 INF]   Platform Admin: admin@syntera.com
[14:35:15 INF]   Password:       (from Seed:PlatformAdminPassword in appsettings)
[14:35:15 INF]
[14:35:15 INF]   Next: cd ../Syntera.Backend && dotnet run
[14:35:15 INF] ════════════════════════════════════════════════════════════════
```

### Troubleshooting DbSetup

| Error | Solusi |
|-------|--------|
| `Login failed for user 'sa'` | Password salah di `appsettings.json` `ConnectionStrings:Platform` |
| `Network-related error` | SQL Server tidak running — start dengan `sudo systemctl start mssql-server` |
| `Cannot find package` | Jalankan `dotnet restore` di `Syntera.DbSetup/` |
| Hanya 1 site yang gagal | Edit connection string di `appsettings.json` `ConnectionStrings:Sites:{code}` |

### Verifikasi manual (opsional)

```bash
# Cek semua database sudah ada
sqlcmd -S localhost,1433 -U sa -P 'Passwordkuat123!' -C -Q "
SELECT name FROM sys.databases WHERE name LIKE 'syntera_%' ORDER BY name;
"

# Cek tabel di platform DB
sqlcmd -S localhost,1433 -U sa -P 'Passwordkuat123!' -C -Q "
USE syntera_master;
SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_NAME;
"

# Cek platform admin sudah di-seed
sqlcmd -S localhost,1433 -U sa -P 'Passwordkuat123!' -C -Q "
USE syntera_master;
SELECT Email, IsEnabled FROM PlatformUsers;
"

# Cek 6 sites sudah di-seed
sqlcmd -S localhost,1433 -U sa -P 'Passwordkuat123!' -C -Q "
USE syntera_master;
SELECT Code, DisplayName, IsEnabled FROM Sites ORDER BY Code;
"
```

---

## Step 4: Jalankan Backend

```bash
cd Syntera.Backend
export ASPNETCORE_ENVIRONMENT=Development
dotnet run
```

Output yang benar:
```
[11:20:12 INF] Now listening on: http://localhost:5296
[11:20:12 INF] Application started. Press Ctrl+C to shut down.
```

Swagger UI: http://localhost:5296/docs

---

## Step 5: Test Login Platform Admin

Dari terminal lain:

```bash
curl -X POST http://localhost:5296/api/auth/login \
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

## Step 6: Jalankan Frontend

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

## Step 7: Konfigurasi LDAP per Site

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

## Step 8: Provision Site Users

**Opsi A: Trigger LDAP Sync** (otomatis, butuh bind credential)

1. Login sebagai `admin@syntera.com` di UI
2. Buka site Kalventis → assign role `site-business-admin` ke user dari LDAP sync
3. Atau: login sebagai site-business-admin → buka **Users** → **Sync LDAP**

**Opsi B: Manual provisioning** (untuk testing tanpa LDAP)

```bash
# Login sebagai platform admin, dapatkan token
TOKEN=$(curl -s -X POST http://localhost:5296/api/auth/login \
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
cd Syntera.Backend
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
cd Syntera.Backend && dotnet publish -c Release -o ./publish
cd Syntera.React && bun run build

# Migration baru (kalau ada perubahan entity)
cd Syntera.Backend
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
