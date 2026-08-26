using Microsoft.Extensions.Logging;
using Syntera.Domain.Enums;

namespace Syntera.Application.Logging;

// ──────────────────────────────────────────────────────────────────────
// Centralised [LoggerMessage] source-generator definitions.
//
// Why one file for everything?
//   1. DRY — every log call site imports from `Syntera.Application.Logging`
//      and references the partial class for its domain. No per-file
//      duplicate boilerplate.
//   2. Auditable — the EventId ranges per domain are visible at a glance,
//      making it trivial to wire Seq/Datadog filters by id.
//   3. Eliminates CA1848 + CA1873 warnings across the codebase —
//      the source generator produces zero-alloc delegates that
//      check IsEnabled before formatting arguments.
// ──────────────────────────────────────────────────────────────────────

// EventId range 1000–1099: Catalog domain
internal static partial class CategoryLogger
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Category created: {Id} ({Slug})")]
    public static partial void LogCategoryCreated(ILogger logger, Guid id, string slug);
}

internal static partial class SupplierLogger
{
    [LoggerMessage(EventId = 1101, Level = LogLevel.Information,
        Message = "Supplier created: {Id} ({Name})")]
    public static partial void LogSupplierCreated(ILogger logger, Guid id, string name);
}

internal static partial class CustomerLogger
{
    [LoggerMessage(EventId = 1201, Level = LogLevel.Information,
        Message = "Customer created: {Id} ({Name})")]
    public static partial void LogCustomerCreated(ILogger logger, Guid id, string name);
}

internal static partial class ProductLogger
{
    [LoggerMessage(EventId = 1301, Level = LogLevel.Information,
        Message = "Product created: {Id} ({Sku})")]
    public static partial void LogProductCreated(ILogger logger, Guid id, string sku);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Information,
        Message = "Stock adjusted: {Sku} {Delta:+0;-0;} → {Balance}")]
    public static partial void LogStockAdjusted(ILogger logger, string sku, int delta, int balance);
}

// EventId range 1400–1499: Inventory domain
internal static partial class InventoryLogger
{
    // Type is the enum, not a pre-stringified value — passing the enum
    // directly lets the source generator skip the ToString() call when
    // the log level is disabled, which is what CA1873 wants.
    [LoggerMessage(EventId = 1401, Level = LogLevel.Information,
        Message = "Inventory movement recorded: {Type} {Qty} on {Sku}")]
    public static partial void LogMovementRecorded(
        ILogger logger, InventoryMovementType type, int qty, string sku);
}

// EventId range 1500–1599: Sales domain
internal static partial class SaleLogger
{
    [LoggerMessage(EventId = 1501, Level = LogLevel.Information,
        Message = "Sale created: {Invoice} (grand total {Total})")]
    public static partial void LogSaleCreated(ILogger logger, string invoice, decimal total);
}

// EventId range 1600–1699: Auth domain
internal static partial class AuthLogger
{
    [LoggerMessage(EventId = 1601, Level = LogLevel.Warning,
        Message = "Failed login attempt for {Email}")]
    public static partial void LogFailedLogin(ILogger logger, string email);

    [LoggerMessage(EventId = 1602, Level = LogLevel.Debug,
        Message = "Refresh token validation failed")]
    public static partial void LogRefreshValidationFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1603, Level = LogLevel.Warning,
        Message = "Login rejected: {Code} {Message}")]
    public static partial void LogLoginRejected(ILogger logger, string code, string message);
}
