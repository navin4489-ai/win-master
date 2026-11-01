using System;
using WinMaster.Core;
using WinMaster.Core.Entities;
using WinMaster.Infrastructure.Services;
using Xunit;

namespace WinMaster.Tests;

/// <summary>Unit tests for the account-safety policy that keeps WhatsApp numbers from
/// being driven into a restriction/ban by campaign volume.</summary>
public class AccountSafetyTests
{
    private static WhatsAppAccount Acc(WarmupState warm = WarmupState.Ready, int dailyLimit = 1000, int sentToday = 0, DateTime? created = null)
        => new WhatsAppAccount { WarmupState = warm, DailyLimit = dailyLimit, SentToday = sentToday, CreatedAt = created ?? DateTime.UtcNow };

    private static AccountAntiBanSettings Ban(int min = 30, int max = 60, int batch = 15, int cooldown = 10, int cap = 50)
        => new AccountAntiBanSettings { MinDelaySec = min, MaxDelaySec = max, BatchSize = batch, CooldownMinutes = cooldown, DailyCap = cap };

    // ---- Pacing floors: the account can never send faster than its anti-ban settings ----

    [Fact]
    public void SafePacing_NeverFasterThanAccountFloor()
    {
        // Campaign asks for a fast 1-2s delay; the account floor is 30-60s → we must honour the floor.
        var (min, max) = AccountSafety.SafePacing(Acc(), Ban(30, 60), campaignMin: 1, campaignMax: 2);
        Assert.True(min >= 30, $"min delay {min} must respect the 30s floor");
        Assert.True(max >= min, "max must be >= min");
    }

    [Fact]
    public void SafePacing_AllowsSlowerCampaignSetting()
    {
        // Campaign wants to be even slower (90-120s) than the floor → keep the slower, safer value.
        var (min, max) = AccountSafety.SafePacing(Acc(), Ban(30, 60), campaignMin: 90, campaignMax: 120);
        Assert.Equal(90, min);
        Assert.Equal(120, max);
    }

    [Fact]
    public void SafePacing_NoBanSettings_UsesConservativeDefaults()
    {
        var (min, max) = AccountSafety.SafePacing(Acc(), ban: null, campaignMin: 1, campaignMax: 2);
        Assert.True(min >= 30);
    }

    [Fact]
    public void SafeBatchSize_CappedByAccount()
    {
        Assert.Equal(15, AccountSafety.SafeBatchSize(Ban(batch: 15), campaignBatch: 50));
        Assert.Equal(10, AccountSafety.SafeBatchSize(Ban(batch: 15), campaignBatch: 10)); // campaign smaller → keep it
    }

    [Fact]
    public void SafeCooldownMinutes_NeverLessThanAccountFloor()
    {
        Assert.Equal(10, AccountSafety.SafeCooldownMinutes(Ban(cooldown: 10), campaignCooldown: 1));
        Assert.Equal(20, AccountSafety.SafeCooldownMinutes(Ban(cooldown: 10), campaignCooldown: 20));
    }

    // ---- Daily cap: warm-up ramp vs ready ----

    [Fact]
    public void DailyCap_WarmingNumber_IsHeldToLowRamp()
    {
        // A brand-new warming number must be capped very low regardless of configured cap.
        var acc = Acc(WarmupState.Warming, dailyLimit: 1000, created: DateTime.UtcNow);
        var cap = AccountSafety.EffectiveDailyCap(acc, Ban(cap: 500));
        Assert.True(cap <= AccountSafety.WarmupReadyCap, "warming cap must stay under the warm-up hard cap");
        Assert.True(cap <= 20, $"day-one warming cap should be tiny, got {cap}");
    }

    [Fact]
    public void DailyCap_WarmingRamp_GrowsWithAge_ButIsCapped()
    {
        // 10 days old → base 10 + 10*5 = 60, still under the warm-up hard cap of 80.
        var acc = Acc(WarmupState.Warming, created: DateTime.UtcNow.AddDays(-10));
        var cap = AccountSafety.EffectiveDailyCap(acc, Ban(cap: 500));
        Assert.Equal(Math.Min(10 + 10 * AccountSafety.WarmupDailyIncrement, AccountSafety.WarmupReadyCap), cap);

        // 100 days old → would be 510, but hard-capped at WarmupReadyCap.
        var old = Acc(WarmupState.Warming, created: DateTime.UtcNow.AddDays(-100));
        Assert.Equal(AccountSafety.WarmupReadyCap, AccountSafety.EffectiveDailyCap(old, Ban(cap: 500)));
    }

    [Fact]
    public void DailyCap_ReadyNumber_UsesConfiguredCap()
    {
        var acc = Acc(WarmupState.Ready, dailyLimit: 1000);
        Assert.Equal(50, AccountSafety.EffectiveDailyCap(acc, Ban(cap: 50)));
    }

    [Fact]
    public void DailyCap_NeverExceedsAccountDailyLimit()
    {
        var acc = Acc(WarmupState.Ready, dailyLimit: 30);
        var cap = AccountSafety.EffectiveDailyCap(acc, Ban(cap: 500));
        Assert.Equal(30, cap);
    }

    // ---- Daily rollover ----

    [Fact]
    public void ShouldResetDaily_TrueOnNewUtcDay()
    {
        var acc = Acc(sentToday: 40);
        acc.LastDailyResetAt = DateTime.UtcNow.Date;         // today
        Assert.False(AccountSafety.ShouldResetDaily(acc, DateTime.UtcNow));

        acc.LastDailyResetAt = DateTime.UtcNow.Date.AddDays(-1); // yesterday
        Assert.True(AccountSafety.ShouldResetDaily(acc, DateTime.UtcNow));
    }

    [Fact]
    public void ResetDaily_ZerosCounter_AndSetsTimestamp()
    {
        var acc = Acc(sentToday: 47);
        var now = DateTime.UtcNow;
        var after = AccountSafety.ResetDaily(acc, now);
        Assert.Equal(0, after);
        Assert.Equal(0, acc.SentToday);
        Assert.Equal(now.Date, acc.LastDailyResetAt!.Value.Date);
    }

    // ---- Restriction detection ----

    [Theory]
    [InlineData("Account is temporarily blocked")]
    [InlineData("Rate limit exceeded, try later")]
    [InlineData("Your account has been restricted for spam")]
    [InlineData("too many messages")]
    [InlineData("banned")]
    [InlineData("HTTP 429 Too Many Requests")]
    public void IsRestrictionError_MatchesWhatsAppSignals(string error)
        => Assert.True(AccountSafety.IsRestrictionError(error));

    [Theory]
    [InlineData("invalid number")]
    [InlineData("not on whatsapp")]
    [InlineData("network timeout")]
    [InlineData("")]
    [InlineData(null)]
    public void IgnoresOrdinarySendFailures(string? error)
        => Assert.False(AccountSafety.IsRestrictionError(error));

    // ---- Failure-streak circuit breaker ----

    [Fact]
    public void FailureStreak_TripsAtThreshold()
    {
        Assert.False(AccountSafety.IsFailureStreak(AccountSafety.MaxConsecutiveFailures - 1));
        Assert.True(AccountSafety.IsFailureStreak(AccountSafety.MaxConsecutiveFailures));
    }

    [Fact]
    public void RestrictionResumeAt_IsAfterCooldown()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(t.Add(AccountSafety.RestrictionCooldown), AccountSafety.RestrictionResumeAt(t));
    }
}
