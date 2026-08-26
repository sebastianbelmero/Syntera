using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syntera.Api.Controllers;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Sales;
using Syntera.Application.Interfaces.Services;
using Syntera.Application.Services;
using Syntera.Application.Validators;

namespace Syntera.Api.Controllers.Sales;

[ApiController]
[Route("api/[controller]")]
public sealed class SalesController : ApiControllerBase
{
    private readonly ISaleService _svc;
    private readonly SaleCreateValidator _validator;
    private readonly ICurrentUserService _current;

    public SalesController(
        ISaleService svc,
        SaleCreateValidator validator,
        ICurrentUserService current)
    {
        _svc = svc;
        _validator = validator;
        _current = current;
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
        var v = await _validator.ValidateAsync(dto);
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
