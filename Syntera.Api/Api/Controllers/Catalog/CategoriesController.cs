using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syntera.Api.Controllers;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Categories;
using Syntera.Application.Services;
using Syntera.Application.Validators;

namespace Syntera.Api.Controllers.Catalog;

[ApiController]
[Route("api/[controller]")]
public sealed class CategoriesController : ApiControllerBase
{
    private readonly ICategoryService _svc;
    private readonly CategoryUpsertValidator _validator;

    public CategoriesController(ICategoryService svc, CategoryUpsertValidator validator)
    {
        _svc = svc;
        _validator = validator;
    }

    [HttpGet]
    public async Task<IActionResult> Page([FromQuery] PageQuery query, CancellationToken ct)
        => Ok(await _svc.PageAsync(query, ct));

    [HttpGet("tree")]
    public async Task<IActionResult> Tree(CancellationToken ct)
        => Ok(await _svc.GetTreeAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var data = await _svc.GetAsync(id, ct);
        return data is null ? Fail("NOT_FOUND", "Category not found.", 404) : Ok(data);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CategoryUpsertDto dto, CancellationToken ct)
    {
        var v = await _validator.ValidateAsync(dto, ct);
        if (!v.IsValid) return Invalid(v.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)));
        var created = await _svc.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, Ok(created));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CategoryUpsertDto dto, CancellationToken ct)
    {
        var v = await _validator.ValidateAsync(dto, ct);
        if (!v.IsValid) return Invalid(v.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)));
        try { return Ok(await _svc.UpdateAsync(id, dto, ct)); }
        catch (Domain.Exceptions.NotFoundException) { return Fail("NOT_FOUND", "Category not found.", 404); }
        catch (Domain.Exceptions.BusinessRuleException ex) { return Fail(ex.Code, ex.Message, 409); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await _svc.DeleteAsync(id, ct); return NoContent(); }
        catch (Domain.Exceptions.NotFoundException) { return Fail("NOT_FOUND", "Category not found.", 404); }
    }
}
