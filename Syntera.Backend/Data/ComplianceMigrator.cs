using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Syntera.Backend.Data;

namespace Syntera.Backend.Data;

/// <summary>
/// COMPLIANCE (Sprint 2.2): Idempotent SQL migrator that adds the schema
/// changes introduced by the compliance hardening (Sprint 2) — namely:
/// <list type="bullet">
/// <item><c>PlatformUsers.TotpSecret</c>, <c>TotpEnabled</c>, <c>PasswordChangedAt</c></item>
/// <item><c>RefreshTokens.LastUsedAt</c> (Platform + all Site DBs)</item>
/// <item><c>AuditLogs.BeforeJson</c>, <c>AfterJson</c>, <c>SignatureMeaning</c>
///   (Platform + all Site DBs — BeforeJson/AfterJson may already exist from
///   initial migration; the migrator uses <c>IF COL_LENGTH</c> to be
///   idempotent)</item>
/// <item><c>PasswordHistory</c> table (Platform DB)</item>
/// <item><c>RoleTemplateApprovals</c> table (Platform DB)</item>
/// </list>
///
/// <para><b>Why a separate migrator instead of an EF Core migration?</b></para>
/// <para>EF Core migrations require auto-generated Designer.cs + ModelSnapshot.cs
/// updates (~1000 lines of boilerplate per context). The migrator approach
/// uses raw SQL with <c>IF COL_LENGTH / IF NOT EXISTS</c> guards, which is
/// fully idempotent and can run on every startup without harm. The EF
/// DbContext model and the DB schema stay in sync because the migrator
/// adds exactly the columns/tables the model expects.</para>
///
/// <para>This is the same pattern as <c>DatabaseInitializer.MigrateOrBaselineAsync</c>
/// — an explicit, code-reviewed escape hatch around the EF migration
/// pipeline for compliance-critical schema additions.</para>
///
/// <para><b>Where it runs:</b></para>
/// <list type="bullet">
/// <item><see cref="Program.cs"/> startup (after <c>MigrateOrBaselineAsync</c> +
///   <c>SeedPlatformAsync</c>) — covers Backend dev/prod.</item>
/// <item><c>Syntera.DbSetup/Program.cs</c> — covers the one-shot DB setup tool.</item>
/// </list>
/// </summary>
public static class ComplianceMigrator
{
    public static async Task ApplyPlatformAsync(PlatformDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        logger?.LogInformation("ComplianceMigrator: applying Platform compliance schema changes...");

        // ── PlatformUsers: MFA + password age ────────────────────────────
        await ExecAsync(db, @"
            IF COL_LENGTH('PlatformUsers', 'TotpSecret') IS NULL
                ALTER TABLE PlatformUsers ADD TotpSecret NVARCHAR(512) NULL;", ct);

        await ExecAsync(db, @"
            IF COL_LENGTH('PlatformUsers', 'TotpEnabled') IS NULL
                ALTER TABLE PlatformUsers ADD TotpEnabled BIT NOT NULL DEFAULT 0;", ct);

        await ExecAsync(db, @"
            IF COL_LENGTH('PlatformUsers', 'PasswordChangedAt') IS NULL
                ALTER TABLE PlatformUsers ADD PasswordChangedAt DATETIME2 NULL;", ct);

        // Backfill PasswordChangedAt for existing users (avoid forcing immediate change).
        await ExecAsync(db, @"
            UPDATE PlatformUsers
            SET PasswordChangedAt = CreatedAt
            WHERE PasswordChangedAt IS NULL;", ct);

        // ── RefreshTokens: LastUsedAt (Platform scope) ──────────────────
        await ExecAsync(db, @"
            IF COL_LENGTH('RefreshTokens', 'LastUsedAt') IS NULL
                ALTER TABLE RefreshTokens ADD LastUsedAt DATETIME2 NULL;", ct);

        // ── AuditLogs: BeforeJson / AfterJson / SignatureMeaning ───────
        // BeforeJson and AfterJson may already exist from the initial migration;
        // SignatureMeaning is new.
        await ExecAsync(db, @"
            IF COL_LENGTH('AuditLogs', 'BeforeJson') IS NULL
                ALTER TABLE AuditLogs ADD BeforeJson NVARCHAR(MAX) NULL;", ct);

        await ExecAsync(db, @"
            IF COL_LENGTH('AuditLogs', 'AfterJson') IS NULL
                ALTER TABLE AuditLogs ADD AfterJson NVARCHAR(MAX) NULL;", ct);

        await ExecAsync(db, @"
            IF COL_LENGTH('AuditLogs', 'SignatureMeaning') IS NULL
                ALTER TABLE AuditLogs ADD SignatureMeaning NVARCHAR(500) NULL;", ct);

        // ── PasswordHistory table ───────────────────────────────────────
        await ExecAsync(db, @"
            IF OBJECT_ID(N'PasswordHistory', N'U') IS NULL
            CREATE TABLE PasswordHistory (
                Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                PlatformUserId  UNIQUEIDENTIFIER NOT NULL,
                PasswordHash    NVARCHAR(255) NOT NULL,
                SetAt           DATETIME2 NOT NULL,
                CreatedAt       DATETIME2 NOT NULL,
                UpdatedAt       DATETIME2 NOT NULL
            );", ct);

        await ExecAsync(db, @"
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PasswordHistory_PlatformUserId_SetAt' AND object_id = OBJECT_ID(N'PasswordHistory', N'U'))
                CREATE INDEX IX_PasswordHistory_PlatformUserId_SetAt ON PasswordHistory (PlatformUserId, SetAt);", ct);

        // ── RoleTemplateApprovals table (two-person rule) ──────────────
        await ExecAsync(db, @"
            IF OBJECT_ID(N'RoleTemplateApprovals', N'U') IS NULL
            CREATE TABLE RoleTemplateApprovals (
                Id                          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                RoleTemplateId              UNIQUEIDENTIFIER NOT NULL,
                RequestedBy                  UNIQUEIDENTIFIER NOT NULL,
                RequestedAt                 DATETIME2 NOT NULL,
                RequestedSnapshotJson       NVARCHAR(MAX) NOT NULL,
                Status                      NVARCHAR(16) NOT NULL DEFAULT 'pending',
                ActionBy                    UNIQUEIDENTIFIER NULL,
                ActionAt                    DATETIME2 NULL,
                RejectionReason             NVARCHAR(2000) NULL,
                RequesterSignatureMeaning   NVARCHAR(500) NULL,
                ApproverSignatureMeaning    NVARCHAR(500) NULL,
                CreatedAt                   DATETIME2 NOT NULL,
                UpdatedAt                   DATETIME2 NOT NULL
            );", ct);

        await ExecAsync(db, @"
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RoleTemplateApprovals_RoleTemplateId_Status' AND object_id = OBJECT_ID(N'RoleTemplateApprovals', N'U'))
                CREATE INDEX IX_RoleTemplateApprovals_RoleTemplateId_Status ON RoleTemplateApprovals (RoleTemplateId, Status);", ct);

        logger?.LogInformation("ComplianceMigrator: Platform compliance schema changes applied.");
    }

    public static async Task ApplySiteAsync(SiteDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        logger?.LogInformation("ComplianceMigrator: applying Site compliance schema changes to {Db}...", db.Database.GetDbConnection().Database);

        // ── RefreshTokens: LastUsedAt (Site scope) ─────────────────────
        await ExecAsync(db, @"
            IF COL_LENGTH('RefreshTokens', 'LastUsedAt') IS NULL
                ALTER TABLE RefreshTokens ADD LastUsedAt DATETIME2 NULL;", ct);

        // ── AuditLogs: BeforeJson / AfterJson / SignatureMeaning ───────
        await ExecAsync(db, @"
            IF COL_LENGTH('AuditLogs', 'BeforeJson') IS NULL
                ALTER TABLE AuditLogs ADD BeforeJson NVARCHAR(MAX) NULL;", ct);

        await ExecAsync(db, @"
            IF COL_LENGTH('AuditLogs', 'AfterJson') IS NULL
                ALTER TABLE AuditLogs ADD AfterJson NVARCHAR(MAX) NULL;", ct);

        await ExecAsync(db, @"
            IF COL_LENGTH('AuditLogs', 'SignatureMeaning') IS NULL
                ALTER TABLE AuditLogs ADD SignatureMeaning NVARCHAR(500) NULL;", ct);

        logger?.LogInformation("ComplianceMigrator: Site compliance schema changes applied to {Db}.", db.Database.GetDbConnection().Database);
    }

    /// <summary>
    /// Apply compliance schema to all enabled site DBs. Called from Program.cs
    /// after Platform compliance migration + seeding.
    /// </summary>
    public static async Task ApplyAllSitesAsync(
        PlatformDbContext platformDb,
        ISiteDbContextFactory siteDbFactory,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var sites = await platformDb.Sites.AsNoTracking()
            .Where(s => s.IsEnabled)
            .Select(s => new { s.Id, s.Code })
            .ToListAsync(ct);

        foreach (var site in sites)
        {
            try
            {
                var siteDb = await siteDbFactory.ResolveForSiteAsync(site.Id, ct);
                await ApplySiteAsync(siteDb, logger, ct);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "ComplianceMigrator: failed to apply to site {Code}.", site.Code);
            }
        }
    }

    private static async Task ExecAsync(DbContext db, string sql, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                                    || ex.Message.Contains("already a column", StringComparison.OrdinalIgnoreCase))
        {
            // Idempotent — ignore "already exists" errors (shouldn't happen due to IF guards, but defensive).
        }
    }
}
