using Microsoft.Extensions.Configuration;
using Syntera.Backend.Services;

namespace Syntera.Backend.Tests;

public sealed class PasswordPolicyTests
{
    private static IPasswordPolicy Create(Action<Dictionary<string, string?>>? tune = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["PasswordPolicy:MinLength"] = "12",
            ["PasswordPolicy:MaxLength"] = "256",
            ["PasswordPolicy:RequireUpper"] = "true",
            ["PasswordPolicy:RequireLower"] = "true",
            ["PasswordPolicy:RequireDigit"] = "true",
            ["PasswordPolicy:RequireSymbol"] = "true",
        };
        tune?.Invoke(settings);
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new PasswordPolicy(config);
    }

    [Fact]
    public void CompliantPassword_Passes()
    {
        var policy = Create();
        Assert.Empty(policy.Validate("CorrectHorse!42"));
    }

    [Theory]
    [InlineData("Short!1A")]                    // below min length (12)
    [InlineData("alllowercase!1a")]             // no uppercase
    [InlineData("ALLUPPERCASE!1A")]             // no lowercase
    [InlineData("NoDigits!Here")]               // no digit
    [InlineData("NoSymbols12345")]              // no symbol
    [InlineData("")]                            // empty
    public void NonCompliantPassword_Fails(string password)
    {
        var policy = Create();
        Assert.NotEmpty(policy.Validate(password));
    }

    [Fact]
    public void TooLongPassword_Fails()
    {
        var policy = Create();
        var tooLong = "Aa1!" + new string('x', 300);
        Assert.NotEmpty(policy.Validate(tooLong));
    }

    [Fact]
    public void AllViolations_ReportedTogether()
    {
        // "short" fails length + upper + digit + symbol — four distinct messages.
        var policy = Create();
        var violations = policy.Validate("short");
        Assert.True(violations.Count >= 4);
    }

    [Fact]
    public void ConfigurableRules_CanBeRelaxed()
    {
        var policy = Create(s =>
        {
            s["PasswordPolicy:MinLength"] = "8";
            s["PasswordPolicy:RequireSymbol"] = "false";
        });
        Assert.Empty(policy.Validate("Abcdefg1"));
    }
}
