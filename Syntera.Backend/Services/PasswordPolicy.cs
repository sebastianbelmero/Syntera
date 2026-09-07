namespace Syntera.Backend.Services;

/// <summary>
/// M7: password policy enforcement for local-credential users (currently
/// only Platform Admin; site users authenticate via LDAP so their policy is
/// AD's, not ours).
///
/// Default rules (configurable via PasswordPolicy:* in appsettings):
/// - MinLength = 12         (NIST 800-63B: 8 minimum, 12+ recommended)
/// - RequireUpper = true    (1+ uppercase letter)
/// - RequireLower = true    (1+ lowercase letter)
/// - RequireDigit = true    (1+ digit)
/// - RequireSymbol = true   (1+ non-alphanumeric)
/// - MaxLength = 256        (defense vs. bcrypt CPU DoS — bcrypt truncates
///   at 72 bytes anyway; anything beyond is wasted CPU on hash path)
///
/// Returns a list of rule-violation messages (empty = password passes all).
/// Callers surface them as a single string joined by '; ' for the
/// BusinessRuleException message, or use them as fieldErrors for an
/// ApiResponse.VALIDATION_FAILED response.
/// </summary>
public interface IPasswordPolicy
{
    /// <summary>
    /// Validate the given plain-text password against the configured policy.
    /// Returns a list of failure messages (empty = password is compliant).
    /// </summary>
    IReadOnlyList<string> Validate(string password);
}

public sealed class PasswordPolicy : IPasswordPolicy
{
    private readonly int _minLength;
    private readonly int _maxLength;
    private readonly bool _requireUpper;
    private readonly bool _requireLower;
    private readonly bool _requireDigit;
    private readonly bool _requireSymbol;

    public PasswordPolicy(Microsoft.Extensions.Configuration.IConfiguration config)
    {
        // All values have safe defaults — config override is optional.
        _minLength = config.GetValue("PasswordPolicy:MinLength", 12);
        _maxLength = config.GetValue("PasswordPolicy:MaxLength", 256);
        _requireUpper = config.GetValue("PasswordPolicy:RequireUpper", true);
        _requireLower = config.GetValue("PasswordPolicy:RequireLower", true);
        _requireDigit = config.GetValue("PasswordPolicy:RequireDigit", true);
        _requireSymbol = config.GetValue("PasswordPolicy:RequireSymbol", true);
    }

    public IReadOnlyList<string> Validate(string password)
    {
        var errors = new List<string>(capacity: 6);

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Password is required.");
            return errors;
        }
        if (password.Length < _minLength)
            errors.Add($"Password must be at least {_minLength} characters long.");
        if (password.Length > _maxLength)
            errors.Add($"Password must not exceed {_maxLength} characters.");
        if (_requireUpper && !password.Any(char.IsUpper))
            errors.Add("Password must contain at least one uppercase letter.");
        if (_requireLower && !password.Any(char.IsLower))
            errors.Add("Password must contain at least one lowercase letter.");
        if (_requireDigit && !password.Any(char.IsDigit))
            errors.Add("Password must contain at least one digit.");
        if (_requireSymbol && !password.Any(c => !char.IsLetterOrDigit(c)))
            errors.Add("Password must contain at least one non-alphanumeric character (symbol).");

        return errors;
    }
}

// Note: there is no FluentValidation PasswordValidator registered here.
// The change-password endpoint calls IPasswordPolicy directly from
// AuthService.ChangePasswordAsync so it can collect ALL violations at
// once and surface them as a single BusinessRuleException message.
// FluentValidation is still used for the boundary DTOs (LoginRequest,
// UserUpsertDto, etc.) — see Models/Dtos/Validators/AuthValidators.cs.
