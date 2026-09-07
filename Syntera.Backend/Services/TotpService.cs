using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace Syntera.Backend.Services;

/// <summary>
/// COMPLIANCE (Sprint 2.3): TOTP (Time-based One-Time Password) service per
/// RFC 6238, used for Multi-Factor Authentication (MFA) on Platform Admin
/// accounts. Implements 21 CFR Part 11 §11.200(a)(1) — "two distinct
/// components" (something the user knows: password; something the user
/// has: TOTP secret on their authenticator app).
///
/// <para>Secret storage: the TOTP secret is encrypted at rest via ASP.NET
/// Core Data Protection (<see cref="IDataProtector"/>). The plaintext
/// secret is only decrypted in-memory at verification time. The DPAPI key
/// ring is persisted to <c>DataProtection:KeyPath</c> (configurable).</para>
///
/// <para>Algorithm:</para>
/// <list type="bullet">
/// <item>HMAC-SHA1 (RFC 6238 default — widest authenticator app support).</item>
/// <item>30-second time step (T0).</item>
/// <item>6-digit code (max compatibility with Google Authenticator,
/// Microsoft Authenticator, Authy, 1Password, etc.).</item>
/// <item>Window: ±1 time step (allows 30s clock drift — standard practice).</item>
/// </list>
///
/// <para>The otpauth:// URL format is used for QR code generation per the
/// de facto standard (Google Authenticator Key URI format).</para>
/// </summary>
public interface ITotpService
{
    /// <summary>Generate a new random TOTP secret (20 bytes, Base32-encoded). Returns plaintext secret + encrypted version for DB storage.</summary>
    (string plaintextBase32, string encryptedForDb) GenerateNewSecret();

    /// <summary>Decrypt the stored secret and verify the supplied TOTP code. Returns true if the code matches within the time window.</summary>
    Task<bool> VerifyCodeAsync(string encryptedSecret, string code, CancellationToken ct = default);

    /// <summary>Decrypt the stored secret and return it as plaintext Base32 (for showing the user during setup). Use sparingly — only in setup flow.</summary>
    string DecryptSecret(string encryptedSecret);

    /// <summary>Build the otpauth:// URL for QR code generation (per Google Authenticator Key URI format).</summary>
    string BuildOtpAuthUrl(string issuer, string accountName, string plaintextBase32Secret);
}

public sealed class TotpService : ITotpService
{
    private readonly IDataProtector _protector;
    private readonly IConfiguration _config;

    // RFC 6238 parameters.
    private const int TimeStepSeconds = 30;
    private const int CodeDigits = 6;
    private const int WindowSteps = 1; // ±1 step = ±30s tolerance

    // Base32 alphabet (RFC 4648) — no padding for cleaner URLs.
    private const string Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public TotpService(IDataProtectionProvider protectionProvider, IConfiguration config)
    {
        _protector = protectionProvider.CreateProtector("Syntera.Totp.v1");
        _config = config;
    }

    public (string plaintextBase32, string encryptedForDb) GenerateNewSecret()
    {
        // Generate 20 bytes (160 bits) of cryptographically random data.
        // RFC 6238 recommends at least 160 bits for HMAC-SHA1.
        var secretBytes = RandomNumberGenerator.GetBytes(20);
        var base32 = ToBase32(secretBytes);
        var encrypted = _protector.Protect(base32);
        return (base32, encrypted);
    }

    public async Task<bool> VerifyCodeAsync(string encryptedSecret, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(encryptedSecret) || string.IsNullOrWhiteSpace(code))
            return false;

        // Normalize the user-entered code (strip whitespace, uppercase).
        code = code.Trim().Replace(" ", "").ToUpperInvariant();

        // Only accept 6-digit codes (most common; some apps allow 8 — we restrict to 6).
        if (code.Length != CodeDigits || !code.All(char.IsDigit))
            return false;

        string secret;
        try
        {
            secret = _protector.Unprotect(encryptedSecret);
        }
        catch
        {
            // Corrupted secret or key ring mismatch.
            return false;
        }

        var secretBytes = FromBase32(secret);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var currentStep = now / TimeStepSeconds;

        // Check current step ± window.
        for (var offset = -WindowSteps; offset <= WindowSteps; offset++)
        {
            var candidateStep = currentStep + offset;
            var expected = ComputeTotp(secretBytes, candidateStep);
            // Constant-time comparison to prevent timing attacks.
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(code)))
            {
                return await Task.FromResult(true);
            }
        }
        return false;
    }

    public string DecryptSecret(string encryptedSecret)
    {
        try
        {
            return _protector.Unprotect(encryptedSecret);
        }
        catch
        {
            return string.Empty;
        }
    }

    public string BuildOtpAuthUrl(string issuer, string accountName, string plaintextBase32Secret)
    {
        // otpauth://totp/Label?parameters
        // Label = issuer:accountName (URL-encoded)
        // Parameters: secret (Base32), issuer, algorithm, digits, period
        var label = $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}";
        return $"otpauth://totp/{label}?secret={plaintextBase32Secret}" +
               $"&issuer={Uri.EscapeDataString(issuer)}" +
               $"&algorithm=SHA1" +
               $"&digits={CodeDigits}" +
               $"&period={TimeStepSeconds}";
    }

    private static string ComputeTotp(byte[] secretBytes, long timeStep)
    {
        // RFC 4226 HOTP — TOTP = HOTP(counter = current_unix_time / T0).
        var counterBytes = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(counterBytes);

        // Dynamic truncation (RFC 4226 §5.3).
        var offset = hash[hash.Length - 1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        var code = binary % (int)Math.Pow(10, CodeDigits);
        return code.ToString($"D{CodeDigits}");
    }

    private static string ToBase32(byte[] bytes)
    {
        var result = new StringBuilder();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result.Append(Base32Chars[(buffer >> bitsLeft) & 0x1F]);
            }
        }
        if (bitsLeft > 0)
        {
            result.Append(Base32Chars[(buffer << (5 - bitsLeft)) & 0x1F]);
        }
        return result.ToString();
    }

    private static byte[] FromBase32(string value)
    {
        value = value.TrimEnd('=').ToUpperInvariant();
        var byteCount = value.Length * 5 / 8;
        var result = new byte[byteCount];
        var buffer = 0;
        var bitsLeft = 0;
        var index = 0;

        foreach (var c in value)
        {
            var charIndex = Base32Chars.IndexOf(c);
            if (charIndex < 0) continue;
            buffer = (buffer << 5) | charIndex;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                result[index++] = (byte)((buffer >> bitsLeft) & 0xFF);
            }
        }
        return result;
    }
}
