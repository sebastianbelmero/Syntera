using FluentValidation;
using Syntera.Backend.Models.Dtos.Auth;

namespace Syntera.Backend.Models.Dtos.Validators;

/// <summary>
/// Validator for the login request. SECURITY (L1):
/// <list type="bullet">
///   <item>Email must be present + valid format (defends against garbage
///     input reaching the LDAP filter builder / bcrypt path).</item>
///   <item>Password must be 1..256 chars. Lower bound = empty password
///     fails fast before bcrypt; upper bound = bcrypt truncates at 72 bytes
///     anyway, so longer passwords just waste CPU (DoS vector).</item>
/// </list>
/// </summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email is required.")
            .MaximumLength(160).WithMessage("Email must not exceed 160 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            // 256 = generous upper bound. bcrypt itself only uses first 72 bytes,
            // so anything beyond that is wasted CPU on the hash path.
            .MaximumLength(256).WithMessage("Password must not exceed 256 characters.");
    }
}

/// <summary>
/// Validator for user upsert (create / update). SECURITY (L1):
/// <list type="bullet">
///   <item>Email: required, valid format, max 160 (matches DB schema).</item>
///   <item>DisplayName: required, 1..160 (matches DB schema).</item>
///   <item>Title: optional, max 160 (matches DB schema).</item>
/// </list>
/// </summary>
public sealed class UserUpsertDtoValidator : AbstractValidator<Models.Dtos.Users.UserUpsertDto>
{
    public UserUpsertDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email is required.")
            .MaximumLength(160).WithMessage("Email must not exceed 160 characters.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(160).WithMessage("Display name must not exceed 160 characters.");

        RuleFor(x => x.Title)
            .MaximumLength(160).WithMessage("Title must not exceed 160 characters.");
    }
}

/// <summary>
/// Validator for granting a direct (ad-hoc) permission. SECURITY (L1):
/// <list type="bullet">
///   <item>Reason: required, min 10 chars (BusinessRuleException also
///     enforces this; validator surfaces the error at the boundary so
///     the controller returns 400 with a structured fieldErrors payload).</item>
///   <item>ExpiresAt: must be in the future, max 90 days from now
///     (BusinessRuleException also enforces — defense in depth).</item>
/// </list>
/// </summary>
public sealed class GrantDirectPermissionDtoValidator : AbstractValidator<Models.Dtos.Users.GrantDirectPermissionDto>
{
    public const int MaxDirectPermissionDays = 90;

    public GrantDirectPermissionDtoValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.PermissionId).NotEmpty().WithMessage("PermissionId is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MinimumLength(10).WithMessage("Reason must be at least 10 characters.")
            .MaximumLength(500).WithMessage("Reason must not exceed 500 characters.");

        RuleFor(x => x.ExpiresAt)
            .Must(expiry => expiry > DateTime.UtcNow)
            .WithMessage("Expiry must be in the future.")
            .Must(expiry => expiry <= DateTime.UtcNow.AddDays(MaxDirectPermissionDays))
            .WithMessage($"Expiry cannot exceed {MaxDirectPermissionDays} days from now.");
    }
}

/// <summary>
/// Validator for assigning a role to a user. SECURITY (L1):
/// <list type="bullet">
///   <item>UserId, RoleId: required (non-empty Guid).</item>
///   <item>ExpiresAt: if set, must be in the future. (Past expiry is a
///     configuration error — the role would be immediately revoked.)</item>
///   <item>Reason: optional, but if present must be ≤ 500 chars (matches
///     the audit log's Reason column width).</item>
/// </list>
/// </summary>
public sealed class AssignRoleDtoValidator : AbstractValidator<Models.Dtos.Users.AssignRoleDto>
{
    public AssignRoleDtoValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.RoleId).NotEmpty().WithMessage("RoleId is required.");

        RuleFor(x => x.ExpiresAt)
            .Must(expiry => expiry == null || expiry > DateTime.UtcNow)
            .WithMessage("Expiry must be in the future.");

        RuleFor(x => x.Reason)
            .MaximumLength(500).When(x => x.Reason is not null)
            .WithMessage("Reason must not exceed 500 characters.");
    }
}
