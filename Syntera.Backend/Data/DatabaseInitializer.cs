using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Syntera.Backend.Data;

/// <summary>
/// Development-startup database initialization that understands the project's
/// migration history.
///
/// The first IAM builds (pre-migrations era) created the Platform DB with
/// <c>Database.EnsureCreatedAsync()</c>. Such a database contains the complete
/// schema but carries NO migrations history, so a plain
/// <c>Database.MigrateAsync()</c> replays the <c>InitialPlatform</c> migration
/// from scratch and crashes on the first <c>CREATE TABLE</c>
/// ("There is already an object named 'AuditLogs' in the database").
///
/// This helper picks the right path instead:
/// <list type="number">
/// <item>No pending migrations → nothing to do.</item>
/// <item>History already records migrations → normal incremental migration.</item>
/// <item>Database missing or empty → normal migration (fresh install).</item>
/// <item>History empty BUT user tables exist (the EnsureCreated era) →
/// <b>baseline</b>: record every embedded migration as applied, then apply
/// any genuinely newer ones. The recorded schema is bit-identical to what the
/// migration would create because both were generated from the same model —
/// only the bookkeeping was missing.</item>
/// </list>
/// Baselining preserves existing data (platform admin, sites, themes, LDAP
/// configs, audit trail) instead of forcing a destructive database reset.
/// </summary>
public static partial class DatabaseInitializer
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Database '{Database}' contains a pre-migrations schema; recorded {Count} migration(s) as applied (baseline).")]
    private static partial void LogBaseline(ILogger logger, string database, int count);

    /// <summary>
    /// Migrates <paramref name="db"/>, baselining pre-migrations databases
    /// (created via EnsureCreated by earlier builds) instead of replaying
    /// their initial migration against an already-populated schema.
    /// </summary>
    public static async Task MigrateOrBaselineAsync(
        DbContext db,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var pending = await db.Database.GetPendingMigrationsAsync(ct);
        if (!pending.Any())
            return;

        var applied = await db.Database.GetAppliedMigrationsAsync(ct);
        if (applied.Any()
            || !await db.GetService<IRelationalDatabaseCreator>().ExistsAsync(ct)
            || !await HasUserTablesAsync(db, ct))
        {
            // Fresh (or empty) database, or one that already tracks
            // migrations — the plain incremental path is safe.
            await db.Database.MigrateAsync(cancellationToken: ct);
            return;
        }

        // Pre-migrations schema detected: tables exist but no migration was
        // ever recorded. Baseline first so MigrateAsync has nothing (or only
        // genuinely newer migrations) left to apply.
        var recorded = await BaselineAsync(db, ct);

        // Resolving the database name parses the connection string (no I/O) —
        // hoisted out of the logging call's argument list per CA1873.
        var databaseName = db.Database.GetDbConnection().Database;
        if (logger is not null)
            LogBaseline(logger, databaseName, recorded);

        await db.Database.MigrateAsync(cancellationToken: ct);
    }

    /// <summary>
    /// Writes one history row per embedded migration so EF considers the
    /// existing schema fully migrated. Uses EF's own history-repository
    /// scripts (correct table name, schema, and column quoting per provider).
    /// </summary>
    private static async Task<int> BaselineAsync(DbContext db, CancellationToken ct)
    {
        var migrations = db.GetService<IMigrationsAssembly>().Migrations;
        var productVersion = typeof(Migration).Assembly.GetName().Version?.ToString(3) ?? "10.0.0";
        var history = db.GetService<IHistoryRepository>();

        var script = history.GetCreateIfNotExistsScript();
        foreach (var id in migrations.Keys.Order(StringComparer.OrdinalIgnoreCase))
            script += "\n" + history.GetInsertScript(new HistoryRow(id, productVersion));

        await db.Database.ExecuteSqlRawAsync(sql: script, cancellationToken: ct);
        return migrations.Count;
    }

    /// <summary>
    /// True when the database already contains user tables (i.e. a schema
    /// exists). EF bookkeeping tables — always named with the
    /// "__EFMigrationsHistory…" prefix — are excluded.
    /// </summary>
    private static async Task<bool> HasUserTablesAsync(DbContext db, CancellationToken ct)
    {
        var count = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE LEFT(name, 2) <> '__'")
            .SingleAsync(ct);
        return count > 0;
    }
}
