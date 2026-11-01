using System.Text.RegularExpressions;
using WinMaster.Core;
using WinMaster.Core.Entities;

namespace WinMaster.Infrastructure.Services;

/// <summary>
/// Central account-protection policy. Every outbound campaign send goes through this so a
/// WhatsApp number can never be driven into a restriction/ban by volume. It decides:
///  - the SAFE pacing floor for an account (never faster than the anti-ban settings),
///  - the effective daily cap (honouring warm-up ramp + anti-ban DailyCap),
///  - whether the daily counter should roll over (midnight UTC),
///  - and whether a send error / failure streak looks like a live restriction.
/// Pure &amp; static so it is trivially unit-tested.
/// </summary>
public static class AccountSafety
{
    // Conservative starting point for a brand-new number. Real WhatsApp trust is earned over
    // weeks; anything above this on day one is how accounts get restricted.
    public const int WarmupStartCap = 10;
    public const int WarmupDailyIncrement = 5;   // +5 allowed per full day of clean history
    public const int WarmupReadyCap = 80;        // never ramp a warming number past this
    public const int WarmupReadyAfterSent = 200; // SentToday-independent lifetime floor to leave warming

    /// <summary>Anti-ban signals WhatsApp returns when an account is limited/restricted/banned.</summary>
    private static readonly Regex RestrictionSignal = new(
        @"restricted|temporarily blocked|too many|rate[- ]?limit|spam|abuse|banned|not allowed|quality|429",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Effective pacing floor for an account. The campaign's own delays are treated as a
    /// CEILING we may exceed for safety: we always send at least as slowly as the anti-ban
    /// settings require. Returns the (min, max) delay in seconds to actually use.
    /// </summary>
    public static (int minDelaySec, int maxDelaySec) SafePacing(WhatsAppAccount acc, AccountAntiBanSettings? ban, int campaignMin, int campaignMax)
    {
        var floorMin = ban?.MinDelaySec ?? 30;
        var floorMax = ban?.MaxDelaySec ?? 60;
        var min = Math.Max(campaignMin, floorMin);
        var max = Math.Max(Math.Max(campaignMax, floorMax), min + 1);
        return (min, max);
    }

    /// <summary>
    /// Effective batch size a campaign may send before a cooldown, capped by the account's
    /// anti-ban batch size so a large campaign cannot fire too many in one burst.
    /// </summary>
    public static int SafeBatchSize(AccountAntiBanSettings? ban, int campaignBatch)
    {
        var cap = ban?.BatchSize ?? 20;
        return Math.Clamp(campaignBatch <= 0 ? cap : Math.Min(campaignBatch, cap), 1, cap);
    }

    /// <summary>Cooldown (minutes) forced between batches, never less than the anti-ban requirement.</summary>
    public static int SafeCooldownMinutes(AccountAntiBanSettings? ban, int campaignCooldown)
    {
        var floor = ban?.CooldownMinutes ?? 10;
        return Math.Max(campaignCooldown, floor);
    }

    /// <summary>
    /// Effective daily cap for an account today. A warming number is held to a low ramp that
    /// grows with clean history; a ready number uses the anti-ban DailyCap (never more than
    /// DailyLimit). Returns the max messages this account may send in the current day.
    /// </summary>
    public static int EffectiveDailyCap(WhatsAppAccount acc, AccountAntiBanSettings? ban)
    {
        if (acc.WarmupState == WarmupState.Warming)
        {
            // Ramp: base + (full days since the account was created) * increment, hard-capped.
            // A brand-new number is held to a tiny volume and grows only with clean history.
            var ageDays = Math.Max(0, (int)(DateTime.UtcNow - acc.CreatedAt).TotalDays);
            var ramp = WarmupStartCap + ageDays * WarmupDailyIncrement;
            return Math.Min(ramp, WarmupReadyCap);
        }
        var cap = ban?.DailyCap ?? 50;
        if (acc.DailyLimit > 0) cap = Math.Min(cap, acc.DailyLimit);
        return Math.Max(1, cap);
    }

    /// <summary>True when the daily counter should roll over (new UTC day since last reset).</summary>
    public static bool ShouldResetDaily(WhatsAppAccount acc, DateTime nowUtc) =>
        acc.LastDailyResetAt is null || acc.LastDailyResetAt.Value.Date != nowUtc.Date;

    /// <summary>Roll the daily counter over for a new day. Returns the reset SentToday (0).</summary>
    public static int ResetDaily(WhatsAppAccount acc, DateTime nowUtc)
    {
        acc.SentToday = 0;
        acc.LastDailyResetAt = nowUtc;
        return 0;
    }

    /// <summary>
    /// True when a send error indicates WhatsApp is restricting/limiting the account.
    /// Used to pause the campaign immediately instead of burning the rest of the list.
    /// </summary>
    public static bool IsRestrictionError(string? error) =>
        !string.IsNullOrWhiteSpace(error) && RestrictionSignal.IsMatch(error);

    /// <summary>
    /// Consecutive-failure circuit breaker. A run of failures against live numbers is the
    /// earliest on-the-wire sign the account is being throttled; stop before it escalates.
    /// </summary>
    public const int MaxConsecutiveFailures = 5;
    public static bool IsFailureStreak(int consecutiveFailures) => consecutiveFailures >= MaxConsecutiveFailures;

    /// <summary>Auto-restriction cool-down: how long a restriction-paused campaign stays parked.</summary>
    public static readonly TimeSpan RestrictionCooldown = TimeSpan.FromHours(6);

    /// <summary>Earliest time a restriction-paused campaign may auto-resume.</summary>
    public static DateTime RestrictionResumeAt(DateTime restrictedAtUtc) => restrictedAtUtc.Add(RestrictionCooldown);

    /// <summary>Builds the user-facing message for a daily-cap pause.</summary>
    public static string DailyCapMessage(int sent, int cap, DateTime resumeAtUtc) =>
        $"Paused to protect this WhatsApp number: daily safe limit reached ({sent}/{cap}). " +
        $"Sending resumes automatically at {resumeAtUtc:HH:mm} UTC.";

    /// <summary>Builds the user-facing message for a restriction pause.</summary>
    public static string RestrictionMessage(string? reason) =>
        "PAUSED — WhatsApp appears to be restricting this account" +
        (string.IsNullOrWhiteSpace(reason) ? "." : $" ({reason}).") +
        " Sending is stopped to avoid a ban. It will auto-retry after the cool-down; scan the QR if the session dropped.";
}
