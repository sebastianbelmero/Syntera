using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Syntera.Application.Interfaces;
using Syntera.Infrastructure.Data;

namespace Syntera.Infrastructure.Data;

/// <summary>
/// Thin wrapper over <see cref="AppDbContext"/> exposing a UnitOfWork
/// boundary. Transactions are created on demand — services call
/// <see cref="BeginTransactionAsync"/> / <see cref="CommitTransactionAsync"/>
/// around multi-step writes (e.g. create sale + decrement stock) to
/// guarantee atomicity. Retries on transient SQL Server failures are
/// handled by <c>EnableRetryOnFailure</c> registered in DI.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(AppDbContext db) => _db = db;

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null) return;
        _transaction = await _db.Database.BeginTransactionAsync(ct);
    }

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is null) return;
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is null) return;
        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public void Dispose()
    {
        _transaction?.Dispose();
        _db.Dispose();
    }
}
