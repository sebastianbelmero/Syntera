using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Syntera.Backend.Data;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Services;
using Syntera.Backend.Tests.TestInfrastructure;

namespace Syntera.Backend.Tests;

public sealed class AuditServiceTests : IDisposable
{
    private readonly SqliteBackedAuditFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task SequentialWrites_FormLinearHashChain()
    {
        for (var i = 0; i < 3; i++)
        {
            var ok = await _fx.Audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: Guid.NewGuid(), ActorEmail: "admin@syntera.com",
                ActorIp: "10.0.0.1", ActorUserAgent: "test",
                Action: "user.create", TargetType: "User", TargetId: Guid.NewGuid().ToString(),
                Outcome: "success", BeforeJson: null, AfterJson: """{"email":"a@b.c"}""",
                SignatureMeaning: "action performed"), ct: default);
            Assert.True(ok);
        }

        var rows = await _fx.Db.AuditLogs.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(3, rows.Count);

        Assert.Equal(string.Empty, rows[0].PreviousHash);
        for (var i = 1; i < rows.Count; i++)
            Assert.Equal(rows[i - 1].Hash, rows[i].PreviousHash);
    }

    [Fact]
    public async Task ParallelWrites_DoNotForkTheChain()
    {
        // P2-E regression: before the per-database chain lock, concurrent
        // writers could read the same "last hash", producing two rows with the
        // SAME PreviousHash — a silent chain fork that breaks tamper-evidence.
        const int count = 20;
        var tasks = Enumerable.Range(0, count).Select(i => _fx.Audit.LogAsync(new AuditEntry(
            SiteId: null, ActorUserId: Guid.NewGuid(), ActorEmail: "parallel@syntera.com",
            ActorIp: "10.0.0.1", ActorUserAgent: "test",
            Action: $"parallel.event.{i}", TargetType: "Test", TargetId: i.ToString(),
            Outcome: "success"))).ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, ok => Assert.True(ok));

        var rows = await _fx.Db.AuditLogs.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(count, rows.Count);

        // No fork: every PreviousHash except the genesis row's "" appears exactly once.
        var previousHashes = rows.Skip(1).Select(r => r.PreviousHash).ToList();
        Assert.Equal(previousHashes.Count, previousHashes.Distinct().Count());

        // And the chain is linear end-to-end.
        for (var i = 1; i < rows.Count; i++)
            Assert.Equal(rows[i - 1].Hash, rows[i].PreviousHash);
    }

    [Fact]
    public async Task HashCovers_BeforeAndAfterJson_AndSignatureMeaning()
    {
        // M3-fix guard: both snapshots + signature meaning participate in the
        // hash, so tampering with any of them invalidates the chain.
        await _fx.Audit.LogAsync(new AuditEntry(
            SiteId: null, ActorUserId: null, ActorEmail: "x@y.z", ActorIp: null, ActorUserAgent: null,
            Action: "user.update", TargetType: "User", TargetId: "42",
            Outcome: "success",
            BeforeJson: """{"isEnabled":false}""",
            AfterJson: """{"isEnabled":true}""",
            SignatureMeaning: "I authorize this user enable action."));

        var row = await _fx.Db.AuditLogs.AsNoTracking().SingleAsync();

        // Recompute the expected hash with the documented canonical formula.
        // SQLite round-trips DateTime with Kind=Unspecified (no Z suffix in
        // "O" format), so normalize before formatting — the ticks are exact.
        var timestampUtc = DateTime.SpecifyKind(row.Timestamp, DateTimeKind.Utc);
        var payload = $"{row.PreviousHash}|{timestampUtc:O}|{row.SiteId}|{row.ActorUserId}|{row.ActorEmail}|{row.Action}|{row.TargetType}|{row.TargetId}|{row.Outcome}|{row.ErrorMessage}|{row.BeforeJson}|{row.AfterJson}|{row.SignatureMeaning}";
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
        Assert.Equal(expected, row.Hash);
    }

    [Fact]
    public async Task AuditLog_IsAppendOnly_UpdateThrows()
    {
        await _fx.Audit.LogAsync(new AuditEntry(
            SiteId: null, ActorUserId: null, ActorEmail: null, ActorIp: null, ActorUserAgent: null,
            Action: "auth.login", TargetType: null, TargetId: null, Outcome: "success"));

        var row = await _fx.Db.AuditLogs.SingleAsync();
        row.Action = "tampered.action";

        Assert.Throws<InvalidOperationException>(() => _fx.Db.SaveChanges());
    }

    private sealed class SqliteBackedAuditFixture : IDisposable
    {
        private readonly SqliteConnection _conn;
        public PlatformDbContext Db { get; }
        public AuditService Audit { get; }

        public SqliteBackedAuditFixture()
        {
            _conn = new SqliteConnection("DataSource=:memory:");
            _conn.Open();
            Db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
                .UseSqlite(_conn)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                .Options);
            Db.Database.EnsureCreated();

            var factory = new TestSiteDbContextFactory();
            var current = new FakeCurrentUserService { IsPlatformAdmin = true };
            Audit = new AuditService(Db, factory, current, NullLogger<AuditService>.Instance);
        }

        public void Dispose()
        {
            Db.Dispose();
            _conn.Dispose();
        }
    }
}
