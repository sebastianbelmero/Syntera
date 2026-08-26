namespace Syntera.Domain.Exceptions;

/// <summary>
/// Thrown when a domain invariant is violated (e.g. selling an expired
/// product, negative stock, illegal state transition). The API layer
/// catches this and maps it to a 409 Conflict response.
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

/// <summary>409 — business rule violation (e.g. selling expired drug).</summary>
public class BusinessRuleException : DomainException
{
    public BusinessRuleException(string code, string message) : base(code, message) { }
}
