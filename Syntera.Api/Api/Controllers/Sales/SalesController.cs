using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntera.Api.Controllers;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Sales;
using Syntera.Application.Interfaces.Services;
using Syntera.Application.Services;
using Syntera.Application.Validators;
using Syntera.Infrastructure.Data;
using DevExtreme.AspNet.Data;
using Syntera.Api.ModelBinding;

namespace Syntera.Api.Controllers.Sales;

[ApiController]
[Route("api/[controller]")]
public sealed class SalesController : ApiControllerBase
{
    private readonly ISaleService _svc;
    private readonly SaleCreateValidator _validator;
    private readonly ICurrentUserService _current;
    private readonly AppDbContext _db;

    public SalesController(
        ISaleService svc,
        SaleCreateValidator validator,
        ICurrentUserService current,
        AppDbContext db)
    {
        _svc = svc;
        _validator = validator;
        _current = current;
        _db = db;
    }

    /// <summary>
    /// DevExtreme-aware grid endpoint. Projects each Sale into a flat
    /// row with the customer name and its line-items pre-flattened
    /// (productName / productSku per item), because the React grid
    /// filters/sorts on <c>customerName</c> and renders the items as a
    /// master-detail sub-table — none of which exists on the raw Sale
    /// entity. Feeding raw entities to DataSourceLoader makes global
    /// search fail with "'customerName' is not a member of type Sale".
    /// </summary>
    [HttpGet("grid")]
    public async Task<IActionResult> Grid(
        DataSourceLoadOptions loadOptions,
        CancellationToken ct)
    {
        var query = _db.Sales
            .AsNoTracking()
            .Select(s => new
            {
                s.Id,
                s.InvoiceNumber,
                s.Status,
                s.SaleDate,
                s.CustomerId,
                CustomerName = s.Customer.Name,
                s.SubTotal,
                s.TaxRate,
                s.TaxAmount,
                s.DiscountAmount,
                s.GrandTotal,
                s.Note,
                ItemCount = s.Items.Count,
                Items = s.Items.Select(i => new
                {
                    i.Id,
                    i.ProductId,
                    ProductName = i.Product.Name,
                    ProductSku = i.Product.Sku,
                    i.Quantity,
                    i.UnitPrice,
                    i.DiscountAmount,
                    i.LineTotal,
                }).ToList(),
                s.CreatedAt,
            });
        var loadResult = await DataSourceLoader.LoadAsync(query, loadOptions, ct);
        return OkRaw(loadResult);
    }

    [HttpGet]
    public async Task<IActionResult> Page([FromQuery] PageQuery query, CancellationToken ct)
        => Ok(await _svc.PageAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var s = await _svc.GetAsync(id, ct);
        return s is null ? Fail("NOT_FOUND", "Sale not found.", 404) : Ok(s);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<IActionResult> Create([FromBody] SaleCreateDto dto, CancellationToken ct)
    {
        var v = await _validator.ValidateAsync(dto, ct);
        if (!v.IsValid) return Invalid(v.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)));
        try
        {
            var created = await _svc.CreateAsync(dto, _current.UserId, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, Ok(created));
        }
        catch (Domain.Exceptions.NotFoundException ex) { return Fail(ex.Code, ex.Message, 404); }
        catch (Domain.Exceptions.BusinessRuleException ex) { return Fail(ex.Code, ex.Message, 409); }
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] SaleStatusUpdateDto dto, CancellationToken ct)
    {
        try { return Ok(await _svc.UpdateStatusAsync(id, dto.Status, ct)); }
        catch (Domain.Exceptions.NotFoundException) { return Fail("NOT_FOUND", "Sale not found.", 404); }
        catch (Domain.Exceptions.BusinessRuleException ex) { return Fail(ex.Code, ex.Message, 409); }
    }
}
