namespace Syntera.Application.DTOs.Dashboard;

public sealed record DashboardSummaryDto(
    int TotalProducts,
    int LowStockProducts,
    int NearExpiryProducts,
    int TotalCustomers,
    int TotalSuppliers,
    decimal TodaySalesAmount,
    int TodaySalesCount,
    decimal MonthSalesAmount,
    int MonthSalesCount,
    decimal YearSalesAmount);

public sealed record SalesTrendPoint(DateTime Date, decimal Amount, int Count);

public sealed record TopProductDto(
    Guid ProductId,
    string ProductName,
    string ProductSku,
    int QuantitySold,
    decimal Revenue);

public sealed record DashboardTrendDto(
    IReadOnlyList<SalesTrendPoint> Last14Days,
    IReadOnlyList<TopProductDto> Top5ProductsThisMonth);
