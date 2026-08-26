using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syntera.Api.Controllers;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Customers;
using Syntera.Application.Services;
using Syntera.Application.Validators;
using Syntera.Infrastructure.Data;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;

namespace Syntera.Api.Controllers.Parties;

[ApiController]
[Route("api/[controller]")]
public sealed class CustomersController : ApiControllerBase
{
    private readonly ICustomerService _svc;
    private readonly CustomerUpsertValidator _validator;
    private readonly AppDbContext _db;

    public CustomersController(
        ICustomerService svc,
        CustomerUpsertValidator validator,
        AppDbContext db)
    {
        _svc = svc;
        _validator = validator;
        _db = db;
    }

    /// <summary>
    /// DevExtreme-aware grid endpoint — raw <c>{ data, totalCount }</c>
    /// shape for AppGrid client-side binding.
    /// </summary>
    [HttpGet("grid")]
    public async Task<IActionResult> Grid(
        [DataSourceRequest] DataSourceLoadOptions loadOptions,
        CancellationToken ct)
    {
        var query = _db.Customers.AsNoTracking().AsQueryable();
        var loadResult = await DataSourceLoader.LoadAsync(query, loadOptions, ct);
        return OkRaw(loadResult);
    }

    [HttpGet]
    public async Task<IActionResult> Page([FromQuery] PageQuery query, CancellationToken ct)
        => Ok(await _svc.PageAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var c = await _svc.GetAsync(id, ct);
        return c is null ? Fail("NOT_FOUND", "Customer not found.", 404) : Ok(c);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<IActionResult> Create([FromBody] CustomerUpsertDto dto, CancellationToken ct)
    {
        var v = await _validator.ValidateAsync(dto, ct);
        if (!v.IsValid) return Invalid(v.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)));
        var created = await _svc.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, Ok(created));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CustomerUpsertDto dto, CancellationToken ct)
    {
        var v = await _validator.ValidateAsync(dto, ct);
        if (!v.IsValid) return Invalid(v.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)));
        try { return Ok(await _svc.UpdateAsync(id, dto, ct)); }
        catch (Domain.Exceptions.NotFoundException) { return Fail("NOT_FOUND", "Customer not found.", 404); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await _svc.DeleteAsync(id, ct); return NoContent(); }
        catch (Domain.Exceptions.NotFoundException) { return Fail("NOT_FOUND", "Customer not found.", 404); }
    }
}
