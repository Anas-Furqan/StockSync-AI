using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StockSyncAI.Api.Services;

public sealed partial class ProductTextNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> UnitTokens =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tab"] = "tablet", ["tabs"] = "tablet", ["tablet"] = "tablet",
            ["tablets"] = "tablet", ["cap"] = "capsule", ["caps"] = "capsule",
            ["capsule"] = "capsule", ["capsules"] = "capsule", ["inj"] = "injection",
            ["injection"] = "injection", ["injections"] = "injection", ["drop"] = "drop",
            ["drops"] = "drop", ["syrup"] = "syrup", ["syrups"] = "syrup",
        };

    public string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormKD).ToLowerInvariant();
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '%'
                ? character
                : ' ');
        }

        var spaced = CompactStrengthPattern().Replace(builder.ToString(), "$1 $2");
        spaced = CompactPackPattern().Replace(spaced, "x $1");
        var tokens = WhitespacePattern().Split(spaced.Trim())
            .Where(token => token.Length > 0)
            .Select(token => UnitTokens.GetValueOrDefault(token, token));
        return string.Join(' ', tokens);
    }

    public IReadOnlySet<string> ExtractStrengths(string normalized) =>
        StrengthPattern().Matches(normalized)
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);

    public IReadOnlySet<string> ExtractDosageForms(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token is "tablet" or "capsule" or "syrup" or
                "injection" or "drop" or "cream" or "ointment" or "suspension" or
                "solution" or "gel" or "spray" or "powder" or "suppository")
            .ToHashSet(StringComparer.Ordinal);

    public string? ExtractPackSize(string? explicitPackSize, string normalizedDescription)
    {
        var normalizedPack = Normalize(explicitPackSize);
        if (normalizedPack.Length > 0)
        {
            var numbers = NumberPattern().Matches(normalizedPack);
            return numbers.Count == 1 ? numbers[0].Value : null;
        }
        var match = PackPattern().Match(normalizedDescription);
        if (!match.Success)
        {
            return null;
        }
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\b(\d+(?:\.\d+)?)(mcg|mg|g|ml|l)\b")]
    private static partial Regex CompactStrengthPattern();

    [GeneratedRegex(@"\bx(\d+(?:\.\d+)?)\b")]
    private static partial Regex CompactPackPattern();

    [GeneratedRegex(@"\b\d+(?:\.\d+)?\s*(?:mcg|mg|g|ml|l|%)\b")]
    private static partial Regex StrengthPattern();

    [GeneratedRegex(@"(?:\bx\s*|\bpack\s*|\b)(\d+(?:\.\d+)?)\s*(?:tablet|capsule|ampoule|vial|sachet|dose|unit)s?\b|\bx\s*(\d+(?:\.\d+)?)\b")]
    private static partial Regex PackPattern();

    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex NumberPattern();
}
