namespace Syntera.Domain.Exceptions;

/// <summary>
/// Thrown when a domain invariant is violated (e.g., user not provisioned,
/// direct permission expiry exceeds 90 days, attempt to grant platform-only
/// permission at site level). The API layer catches this and maps to a 409.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    public DomainException(string code, string message, Exception inner) : base(message, inner)
    {
        Code = code;
    }
}

/// <summary>404 — entity not found. Maps to a 404 in the API layer.</summary>
public class NotFoundException : DomainException
{
    public NotFoundException(string entity, object key)
        : base("NOT_FOUND", $"{entity} with key '{key}' was not found.") { }
}

/// <summary>409 — business rule violation.</summary>
public class BusinessRuleException : DomainException
{
    public BusinessRuleException(string code, string message) : base(code, message) { }
}

/// <summary>401 — authentication failed (LDAP bind failed, user not provisioned, etc.).</summary>
public class AuthenticationException : DomainException
{
    public AuthenticationException(string code, string message) : base(code, message) { }
}

/// <summary>403 — authenticated but lacks required permission.</summary>
public class AuthorizationException : DomainException
{
    public AuthorizationException(string code, string message) : base(code, message) { }
}
