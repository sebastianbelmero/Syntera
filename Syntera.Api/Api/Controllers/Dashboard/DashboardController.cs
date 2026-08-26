using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syntera.Api.Controllers;
using Syntera.Application.Common;
using Syntera.Application.Services;

namespace Syntera.Api.Controllers.Dashboard;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DashboardController : ApiControllerBase
{
    private readonly IDashboardService _svc;

    public DashboardController(IDashboardService svc) => _svc = svc;

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
        => Ok(await _svc.GetSummaryAsync(ct));

    [HttpGet("trend")]
    public async Task<IActionResult> Trend(CancellationToken ct)
        => Ok(await _svc.GetTrendAsync(ct));
}
