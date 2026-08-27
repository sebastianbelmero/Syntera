using Microsoft.AspNetCore.DataProtection;

namespace Syntera.Infrastructure.Security;

/// <summary>
/// Encrypts/decrypts LDAP bind credentials using ASP.NET Core Data Protection.
/// The DPAPI key ring is persisted to disk (configured at startup). The
/// purpose string "Syntera.LdapCredentials.v1" scopes the encryption so
/// the same key cannot be (mis)used for other purposes.
///
/// Encrypted output is base64-encoded for safe storage in NVARCHAR columns.
/// </summary>
public interface ILdapConfigProtector
{
    string Protect(string plain);
    string Unprotect(string encrypted);
}

public sealed class LdapConfigProtector : ILdapConfigProtector
{
    private readonly IDataProtector _protector;
    public const string Purpose = "Syntera.LdapCredentials.v1";

    public LdapConfigProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plain)
        => Convert.ToBase64String(_protector.Protect(System.Text.Encoding.UTF8.GetBytes(plain)));

    public string Unprotect(string encrypted)
        => System.Text.Encoding.UTF8.GetString(_protector.Unprotect(Convert.FromBase64String(encrypted)));
}
