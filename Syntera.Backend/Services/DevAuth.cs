using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Syntera.Backend.Services;

/// <summary>
/// DEV AUTH BACKDOOR (Development-only — never compiled into a production path).
///
/// When active, a site-user login verifies the TYPED password against a fixed
/// dev LDAP account (DevAuth:LdapEmail) instead of the entered email's own
/// LDAP entry. The session, profile, tokens and audit trail are still issued
/// for the ENTERED email — only the LDAP password check is redirected. This
/// lets a developer log in as ANY pre-provisioned site user (to inspect
/// role-specific pages) while only knowing ONE real password.
///
/// <para>Example (config below): typing <c>abc@kalventis.com</c> + any password
/// at the login screen authenticates the session as <c>abc@kalventis.com</c>,
/// but the LDAP bind runs as <c>sebastian.sitorus@kalventis.com</c> with the
/// typed password.</para>
///
/// <para>HARD SAFETY GATES — this can never run in Production:</para>
/// <list type="number">
///   <item><b>Program.cs fail-fast</b>: the app refuses to start in the
///     Production environment when <c>DevAuth:Enabled=true</c> — a prod config
///     carrying this flag is a deployment error (or an intrusion), not a
///     tuning knob.</item>
///   <item><see cref="Resolve"/> ignores the flag unless the host environment
///     IS Development — even if the config somehow survives gate 1.</item>
///   <item>The base appsettings.json has NO DevAuth section; the flag lives
///     only in appsettings.Development.json.</item>
/// </list>
///
/// <para>TOGGLE (takes effect on the next login — appsettings hot-reloads):</para>
/// <list type="bullet">
///   <item>Off: appsettings.Development.json → <c>"DevAuth": { "Enabled": false }</c></item>
///   <item>Off (restart): env var <c>SYNTERA_DevAuth__Enabled=false</c></item>
///   <item>Switch dev account: change <c>DevAuth:LdapEmail</c></item>
/// </list>
/// </summary>
public sealed record DevAuthOverride(bool IsEnabled, string LdapEmail)
{
    /// <summary>Inactive override — the default everywhere except Development.</summary>
    public static readonly DevAuthOverride None = new(false, "");

    /// <summary>
    /// Resolve the override from configuration. Returns <see cref="None"/>
    /// unless ALL of the following hold:
    ///   1. the host environment is Development,
    ///   2. DevAuth:Enabled is true,
    ///   3. DevAuth:LdapEmail is a non-empty string containing '@'.
    /// Read per-request (scoped services / controller actions), so flipping
    /// the config while the app runs takes effect without a restart.
    /// </summary>
    public static DevAuthOverride Resolve(IHostEnvironment? env, IConfiguration? config)
    {
        if (env is null || !env.IsDevelopment())
            return None;

        if (config is null || !config.GetValue<bool>("DevAuth:Enabled"))
            return None;

        var email = config.GetValue<string?>("DevAuth:LdapEmail")?
            .Trim()
            .ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return None;

        return new DevAuthOverride(true, email);
    }
}
