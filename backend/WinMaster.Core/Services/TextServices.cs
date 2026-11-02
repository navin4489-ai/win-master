using System.Text;
using System.Text.RegularExpressions;

namespace WinMaster.Core.Services;

/// <summary>Resolves spintax expressions like {Hello|Hi|Greetings} by picking a random option.</summary>
public class SpintaxService
{
    private readonly Random _rng;
    public SpintaxService(Random? rng = null) => _rng = rng ?? Random.Shared;

    public string Spin(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        // Resolve innermost braces first so nested spintax works.
        var pattern = new Regex(@"\{([^{}]*)\}");
        var text = input;
        var guard = 0;
        while (pattern.IsMatch(text) && guard++ < 100)
        {
            text = pattern.Replace(text, m =>
            {
                var options = m.Groups[1].Value.Split('|');
                // A single option with no pipe is treated as a literal placeholder, leave as-is.
                if (options.Length == 1) return "\uE000" + options[0] + "\uE001";
                return options[_rng.Next(options.Length)];
            });
        }
        return text.Replace("\uE000", "{").Replace("\uE001", "}");
    }
}

/// <summary>Substitutes {{Name}}, {{InvoiceNo}}, {{CustomField}} tokens from a row dictionary.</summary>
public class PersonalizationService
{
    private static readonly Regex TokenRegex = new(@"\{\{\s*([A-Za-z0-9_ ]+?)\s*\}\}", RegexOptions.Compiled);

    public string Apply(string template, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(template)) return template;
        return TokenRegex.Replace(template, m =>
        {
            var key = m.Groups[1].Value.Trim();
            foreach (var kv in values)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value ?? "";
            return ""; // unknown token resolves to empty
        });
    }
}

/// <summary>Phone normalization + masking so numbers never leak into logs.</summary>
public static class PhoneUtils
{
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new StringBuilder();
        foreach (var c in raw)
            if (char.IsDigit(c)) sb.Append(c);
            else if (c == '+' && sb.Length == 0) sb.Append('+');
        return sb.ToString();
    }

    public static bool IsValid(string raw)
    {
        var n = Normalize(raw).TrimStart('+');
        // Real international mobile numbers are 10–15 digits (E.164).
        return n.Length >= 10 && n.Length <= 15;
    }

    /// <summary>Normalizes local Indian mobile input to +91XXXXXXXXXX while preserving
    /// an explicitly supplied international country code.</summary>
    public static string NormalizeIndiaDefault(string raw)
    {
        var value = (raw ?? "").Trim();
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits)) return value;
        // An explicit international prefix ("+" in the source) is always respected as-is.
        if (value.StartsWith("+")) return "+" + digits;
        if (digits.StartsWith("0091")) digits = digits[2..];
        else if (digits.StartsWith("91") && digits.Length == 12) return "+" + digits;
        if (digits.StartsWith("0") && digits.Length == 11) digits = digits[1..]; // trunk-prefixed 0XXXXXXXXXX
        // A bare 10-digit Indian mobile (starts 6–9) → default to +91. This is the common case
        // for pasted/imported local numbers and prevents "recipient is not on WhatsApp" failures.
        if (digits.Length == 10 && digits[0] is >= '6' and <= '9') return "+91" + digits;
        // Already has the 91 country code (12 digits) after cleanup.
        if (digits.StartsWith("91") && digits.Length == 12) return "+" + digits;
        // Fallback: keep digits; add "+" only if it already looked international.
        return value.StartsWith("+") ? "+" + digits : digits;
    }

    public static string Mask(string phone)
    {
        var n = Normalize(phone);
        if (n.Length <= 4) return new string('*', n.Length);
        return n[..2] + new string('*', Math.Max(0, n.Length - 4)) + n[^2..];
    }
}
