using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syntera.Api.Controllers;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Auth;
using Syntera.Application.Logging;
using Syntera.Application.Services;
using Syntera.Application.Validators;

namespace Syntera.Api.Controllers.Auth;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;
    private readonly LoginRequestValidator _loginValidator;
    private readonly ILogger<AuthController> _log;

    public AuthController(
        IAuthService auth,
        LoginRequestValidator loginValidator,
        ILogger<AuthController> log)
    {
        _auth = auth;
        _loginValidator = loginValidator;
        _log = log;
    }

    /// <summary>Authenticate and obtain a JWT + refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var validation = await _loginValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return Invalid(validation.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)));

        try
        {
            var result = await _auth.LoginAsync(req, ct);
            return Ok(result);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            AuthLogger.LogLoginRejected(_log, ex.Code, ex.Message);
            return Unauthorized(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }

    /// <summary>Exchange a refresh token for a new access token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.RefreshToken))
            return BadRequest(ApiResponse<object>.Fail("EMPTY_TOKEN", "Refresh token is required."));

        try
        {
            var result = await _auth.RefreshAsync(req.RefreshToken, ct);
            return Ok(result);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Unauthorized(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }
}
