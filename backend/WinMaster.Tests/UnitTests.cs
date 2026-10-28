using WinMaster.Core;
using WinMaster.Core.Services;
using WinMaster.Infrastructure.Services;
using Xunit;

namespace WinMaster.Tests;

public class TextServiceTests
{
    [Fact]
    public void Spintax_PicksOneOption()
    {
        var spin = new SpintaxService(new Random(1));
        var result = spin.Spin("{Hello|Hi|Hey} there");
        Assert.Contains(result.Replace(" there", ""), new[] { "Hello", "Hi", "Hey" });
        Assert.EndsWith("there", result);
    }

    [Fact]
    public void Spintax_Nested_Resolves()
    {
        var spin = new SpintaxService(new Random(5));
        var result = spin.Spin("{Good {morning|day}|Hello}");
        Assert.DoesNotContain("{", result);
        Assert.DoesNotContain("|", result);
    }

    [Fact]
    public void Spintax_LeavesPlaceholdersUntouched()
    {
        var spin = new SpintaxService(new Random(2));
        var result = spin.Spin("Hi {{Name}}");
        Assert.Equal("Hi {{Name}}", result);
    }

    [Fact]
    public void Personalization_SubstitutesTokens()
    {
        var p = new PersonalizationService();
        var fields = new Dictionary<string, string> { ["Name"] = "Sam", ["InvoiceNo"] = "INV-9" };
        Assert.Equal("Hi Sam, invoice INV-9", p.Apply("Hi {{Name}}, invoice {{InvoiceNo}}", fields));
    }

    [Fact]
    public void Personalization_UnknownToken_Empties()
    {
        var p = new PersonalizationService();
        Assert.Equal("Hi ", p.Apply("Hi {{Missing}}", new Dictionary<string, string>()));
    }

    [Theory]
    [InlineData("+1 (555) 010-0001", true)]
    [InlineData("12345", false)]
    [InlineData("abc", false)]
    public void Phone_Validation(string input, bool expected) => Assert.Equal(expected, PhoneUtils.IsValid(input));

    [Fact]
    public void Phone_Mask_HidesMiddle()
    {
        var masked = PhoneUtils.Mask("+15550100001");
        Assert.StartsWith("+1", masked);
        Assert.Contains("*", masked);
        Assert.EndsWith("01", masked);
    }
}

public class SecurityTests
{
    [Fact]
    public void Password_HashVerify_RoundTrips()
    {
        var hash = PasswordHasher.Hash("Win@12345");
        Assert.True(PasswordHasher.Verify("Win@12345", hash));
        Assert.False(PasswordHasher.Verify("wrong", hash));
    }

    [Fact]
    public void License_KeyFormat()
    {
        var signer = new LicenseSigner("secret");
        var key = signer.GenerateKey(LicenseType.User);
        Assert.StartsWith("WM-", key);
        Assert.Equal(5, key.Split('-').Length); // WM + 4 groups of 4
    }

    [Fact]
    public void License_SignVerify_Succeeds()
    {
        var signer = new LicenseSigner("secret");
        var exp = DateTime.UtcNow.AddDays(30);
        var token = signer.Sign("WM-KEY", "HWID-1", exp);
        Assert.True(signer.Verify("WM-KEY", "HWID-1", exp, token));
        Assert.False(signer.Verify("WM-KEY", "OTHER-HWID", exp, token));
    }
}

public class RuleMatchTests
{
    private static WinMaster.Core.Entities.AutoReplyRule R(MatchType t, string p, int prio) =>
        new() { MatchType = t, Pattern = p, Priority = prio, IsActive = true, ResponseText = p };

    [Fact]
    public void Exact_Match()
    {
        var rules = new[] { R(MatchType.Exact, "hi", 1) };
        Assert.NotNull(RuleService.Match(rules, "HI"));
        Assert.Null(RuleService.Match(rules, "hello"));
    }

    [Fact]
    public void Wildcard_Match()
    {
        var rules = new[] { R(MatchType.Wildcard, "*price*", 1) };
        Assert.NotNull(RuleService.Match(rules, "what is the price now"));
    }

    [Fact]
    public void Regex_Match_RespectsPriority()
    {
        var rules = new[] { R(MatchType.Regex, "^stop$", 5), R(MatchType.Exact, "stop", 1) };
        var m = RuleService.Match(rules, "stop");
        Assert.Equal(1, m!.Priority); // lower priority value wins
    }

    [Fact]
    public void InvalidRegex_IsRejected()
        => Assert.Throws<BusinessException>(() => RuleService.ValidateRegex("("));
}

public class HierarchyTests
{
    [Theory]
    [InlineData(UserRole.SuperAdmin, UserRole.Admin, true)]
    [InlineData(UserRole.Admin, UserRole.Reseller, true)]
    [InlineData(UserRole.Reseller, UserRole.User, true)]
    [InlineData(UserRole.Reseller, UserRole.Admin, false)]
    [InlineData(UserRole.User, UserRole.User, false)]
    public void CanManage(UserRole actor, UserRole target, bool expected)
        => Assert.Equal(expected, Hierarchy.CanManage(actor, target));
}
