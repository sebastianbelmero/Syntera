using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syntera.Api.Controllers;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Inventory;
using Syntera.Application.DTOs.Products;
using Syntera.Application.Interfaces.Services;
using Syntera.Application.Services;
using Syntera.Application.Validators;

namespace Syntera.Api.Controllers.Catalog;

[ApiController]
[Route("api/[controller]")]
public sealed class ProductsController : ApiControllerBase
{
    private readonly IProductService _svc;
    private readonly IInventoryService _inv;
    private readonly ProductUpsertValidator _validator;
    private readonly ICurrentUserService _current;

    public ProductsController(
        IProductService svc,
        IInventoryService inv,
        ProductUpsertValidator validator,
        ICurrentUserService current)
    {
        _svc = svc;
        _inv = inv;
        _validator = validator;
        _current = current;
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] Guid? supplierId,
        [FromQuery] bool? activeOnly,
        [FromQuery] PageQuery query,
        CancellationToken ct)
    {
        var result = await _svc.SearchAsync(search, categoryId, supplierId, activeOnly, query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var p = await _svc.GetAsync(id, ct);
        return p is null ? Fail("NOT_FOUND", "Product not found.", 404) : Ok(p);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Pharmacist")]
    public async Task<IActionResult> Create([FromBody] ProductUpsertDto dto, CancellationToken ct)
    {
        var v = await _validator.ValidateAsync(dto);
        if (!v.IsValid) return Invalid(v.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)));
        try
        {
            var created = await _svc.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, Ok(created));
        }
        catch (Domain.Exceptions.BusinessRuleException ex)
        {
            return Fail(ex.Code, ex.Message, 409);
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Pharmacist")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ProductUpsertDto dto, CancellationToken ct)
    {
        var v = await _validator.ValidateAsync(dto);
        if (!v.IsValid) return Invalid(v.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)));
        try { return Ok(await _svc.UpdateAsync(id, dto, ct)); }
        catch (Domain.Exceptions.NotFoundException) { return Fail("NOT_FOUND", "Product not found.", 404); }
        catch (Domain.Exceptions.BusinessRuleException ex) { return Fail(ex.Code, ex.Message, 409); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await _svc.DeleteAsync(id, ct); return NoContent(); }
        catch (Domain.Exceptions.NotFoundException) { return Fail("NOT_FOUND", "Product not found.", 404); }
    }

    [HttpPost("{id:guid}/stock")]
    [Authorize(Roles = "Admin,Pharmacist")]
    public async Task<IActionResult> AdjustStock(Guid id, [FromBody] ProductStockAdjustDto dto, CancellationToken ct)
    {
        try { return Ok(await _svc.AdjustStockAsync(id, dto, _current.UserId, ct)); }
        catch (Domain.Exceptions.NotFoundException) { return Fail("NOT_FOUND", "Product not found.", 404); }
        catch (Domain.Exceptions.BusinessRuleException ex) { return Fail(ex.Code, ex.Message, 409); }
    }
}

[ApiController]
[Route("api/[controller]")]
public sealed class InventoryController : ApiControllerBase
{
    private readonly IInventoryService _svc;
    private readonly ICurrentUserService _current;

    public InventoryController(IInventoryService svc, ICurrentUserService current)
    {
        _svc = svc;
        _current = current;
    }

    [HttpGet]
    public async Task<IActionResult> Page([FromQuery] PageQuery query, CancellationToken ct)
        => Ok(await _svc.PageAsync(query, ct));

    [HttpGet("product/{productId:guid}")]
    public async Task<IActionResult> History(Guid productId, CancellationToken ct)
        => Ok(await _svc.HistoryAsync(productId, ct));

    [HttpPost]
    [Authorize(Roles = "Admin,Pharmacist")]
    public async Task<IActionResult> Record([FromBody] InventoryAdjustmentRequest req, CancellationToken ct)
    {
        try { return Ok(await _svc.RecordAsync(req, _current.UserId, ct)); }
        catch (Domain.Exceptions.NotFoundException) { return Fail("NOT_FOUND", "Product not found.", 404); }
        catch (Domain.Exceptions.BusinessRuleException ex) { return Fail(ex.Code, ex.Message, 409); }
    }
}
