using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Syntera.Application.DTOs.Dashboard;
using Syntera.Application.Interfaces;
using Syntera.Domain.Enums;

namespace Syntera.Application.Services;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default);
    Task<DashboardTrendDto> GetTrendAsync(CancellationToken ct = default);
}

public sealed class DashboardService : IDashboardService
{
    private readonly IProductRepository _products;
    private readonly ICustomerRepository _customers;
    private readonly ISupplierRepository _suppliers;
    private readonly ISaleRepository _sales;
    private readonly ILogger<DashboardService> _log;

    public DashboardService(
        IProductRepository products,
        ICustomerRepository customers,
        ISupplierRepository suppliers,
        ISaleRepository sales,
        ILogger<DashboardService> log)
    {
        _products = products;
        _customers = customers;
        _suppliers = suppliers;
        _sales = sales;
        _log = log;
    }

    // Note: repository contracts don't expose raw DbSet, so this service
    // relies on the repository-level helpers added to AppDbContext via
    // a dashboard view. In a future iteration we expose a dedicated
    // IDashboardReadModel interface backed by raw SQL or Dapper for
    // perf-sensitive aggregations.
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var allProducts = await _products.ListAsync(ct);
        var allCustomers = await _customers.ListAsync(ct);
        var allSuppliers = await _suppliers.ListAsync(ct);

        var productStocks = new Dictionary<Guid, int>();
        foreach (var p in allProducts)
            productStocks[p.Id] = await _products.GetStockAsync(p.Id, ct);

        var utcNow = DateTime.UtcNow;
        var todayStart = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc);
        var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearStart = new DateTime(utcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Sales already include header totals (SubTotal / GrandTotal) — no need to load Items.
        // We filter out cancelled sales from financial aggregates.
        var allSales = (await _sales.ListAsync(ct))
            .Where(s => s.Status != SaleStatus.Cancelled && s.SaleDate.HasValue).ToList();
        var today = allSales.Where(s => s.SaleDate!.Value >= todayStart).ToList();
        var month = allSales.Where(s => s.SaleDate!.Value >= monthStart).ToList();
        var year = allSales.Where(s => s.SaleDate!.Value >= yearStart).ToList();

        return new DashboardSummaryDto(
            TotalProducts: allProducts.Count,
            LowStockProducts: allProducts.Count(p => productStocks[p.Id] <= p.ReorderLevel),
            NearExpiryProducts: allProducts.Count(p => p.ExpiryDate.HasValue && p.ExpiryDate.Value < utcNow.AddDays(30)),
            TotalCustomers: allCustomers.Count,
            TotalSuppliers: allSuppliers.Count,
            TodaySalesAmount: today.Sum(s => s.GrandTotal),
            TodaySalesCount: today.Count,
            MonthSalesAmount: month.Sum(s => s.GrandTotal),
            MonthSalesCount: month.Count,
            YearSalesAmount: year.Sum(s => s.GrandTotal));
    }

    public async Task<DashboardTrendDto> GetTrendAsync(CancellationToken ct = default)
    {
        var end = DateTime.UtcNow;
        var start = end.AddDays(-14);

        // Load sales with Items included in a single round-trip — Items are
        // needed for Top-5 product aggregation.
        var sales = (await _sales.ListWithItemsAsync(start, ct))
            .Where(s => s.Status != SaleStatus.Cancelled).ToList();

        var trend = new List<SalesTrendPoint>();
        for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
        {
            var day = d;
            var daySales = sales.Where(s => s.SaleDate!.Value.Date == day).ToList();
            trend.Add(new SalesTrendPoint(day, daySales.Sum(s => s.GrandTotal), daySales.Count));
        }

        var monthStart = new DateTime(end.Year, end.Month, 1);
        var top = sales
            .Where(s => s.SaleDate.HasValue && s.SaleDate.Value >= monthStart)
            .SelectMany(s => s.Items, (sale, item) => new { sale, item })
            .GroupBy(x => new { x.item.ProductId, x.item.Product?.Name, x.item.Product?.Sku })
            .Select(g => new TopProductDto(
                g.Key.ProductId,
                g.Key.Name ?? string.Empty,
                g.Key.Sku ?? string.Empty,
                g.Sum(x => x.item.Quantity),
                g.Sum(x => x.item.LineTotal)))
            .OrderByDescending(p => p.QuantitySold)
            .Take(5)
            .ToList();

        return new DashboardTrendDto(trend, top);
    }
}
