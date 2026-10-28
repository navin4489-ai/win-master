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

    public static string Mask(string phone)
    {
        var n = Normalize(phone);
        if (n.Length <= 4) return new string('*', n.Length);
        return n[..2] + new string('*', Math.Max(0, n.Length - 4)) + n[^2..];
    }
}
