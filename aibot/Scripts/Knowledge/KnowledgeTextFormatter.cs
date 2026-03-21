using System.Globalization;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace aibot.Scripts.Knowledge;

public static class KnowledgeTextFormatter
{
    private static readonly Regex PlaceholderRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex ImageTagRegex = new(@"\[img\](.*?)\[/img\]", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex RichTagRegex = new(@"\[(?:/?[a-z_]+)(?:=[^\]]+)?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WhitespaceRegex = new(@"[ \t]+", RegexOptions.Compiled);

    public static string FormatCardText(CardGuideEntry card, string? rawText)
    {
        var model = FindBySlug(ModelDb.AllCards, card.Slug);
        return FormatModelBackedText(
            rawText,
            model is null ? null : () => model.GetDescriptionForPile(PileType.Deck),
            model is null ? null : BuildCardVariables(model));
    }

    public static string FormatRelicText(RelicGuideEntry relic, string? rawText)
    {
        var model = FindBySlug(ModelDb.AllRelics, relic.Slug);
        return FormatModelBackedText(
            rawText,
            model is null ? null : () => model.DynamicDescription.GetFormattedText(),
            model is null ? null : BuildCommonVariables(model.DynamicVars, includeIconVars: true));
    }

    public static string FormatPotionText(PotionEntry potion, string? rawText)
    {
        var model = FindBySlug(ModelDb.AllPotions, potion.Slug);
        return FormatModelBackedText(
            rawText,
            model is null ? null : () => model.DynamicDescription.GetFormattedText(),
            model is null ? null : BuildCommonVariables(model.DynamicVars, includeIconVars: true));
    }

    public static string FormatPowerText(PowerEntry power, string? rawText)
    {
        var model = FindBySlug(ModelDb.AllPowers, power.Slug);
        if (model is null)
        {
            return FormatPlainText(rawText);
        }

        var variables = BuildCommonVariables(model.DynamicVars, includeIconVars: true);
        variables["Amount"] = model.Amount;
        return FormatModelBackedText(rawText, null, variables);
    }

    public static string FormatEventText(EventEntry gameEvent, string? rawText)
    {
        var model = FindBySlug(ModelDb.AllEvents, gameEvent.Slug);
        return FormatModelBackedText(rawText, null, model is null ? null : BuildCommonVariables(model.DynamicVars));
    }

    public static string FormatEnchantmentText(EnchantmentEntry enchantment, string? rawText)
    {
        var model = FindBySlug(ModelDb.DebugEnchantments, enchantment.Slug);
        if (model is null)
        {
            return FormatPlainText(rawText);
        }

        var variables = BuildCommonVariables(model.DynamicVars, includeIconVars: true);
        variables["Amount"] = model.Amount;
        return FormatModelBackedText(rawText, () => model.DynamicDescription.GetFormattedText(), variables);
    }

    public static string FormatPlainText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        var languageHint = DetectLanguage(rawText);
        var replaced = ReplacePlaceholders(rawText, variables: null, languageHint);
        return SanitizeRichText(replaced);
    }

    private static string FormatModelBackedText(string? rawText, Func<string>? runtimeFallback, IReadOnlyDictionary<string, object?>? variables)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return runtimeFallback is null ? string.Empty : SanitizeRichText(runtimeFallback());
        }

        var languageHint = DetectLanguage(rawText);
        var replaced = ReplacePlaceholders(rawText, variables, languageHint);
        var sanitized = SanitizeRichText(replaced);
        if (!LooksUnresolved(sanitized))
        {
            return sanitized;
        }

        return runtimeFallback is null ? sanitized : SanitizeRichText(runtimeFallback());
    }

    private static Dictionary<string, object?> BuildCardVariables(CardModel model)
    {
        var variables = BuildCommonVariables(model.DynamicVars, includeIconVars: true);
        variables["OnTable"] = false;
        variables["InCombat"] = false;
        variables["IsTargeting"] = false;
        return variables;
    }

    private static Dictionary<string, object?> BuildCommonVariables(DynamicVarSet dynamicVars, bool includeIconVars = false)
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in dynamicVars)
        {
            variables[pair.Key] = pair.Value;
        }

        if (includeIconVars)
        {
            variables["energyPrefix"] = "⚡";
            variables["singleStarIcon"] = "★";
        }

        return variables;
    }

    private static TModel? FindBySlug<TModel>(IEnumerable<TModel> models, string slug) where TModel : AbstractModel
    {
        var normalizedSlug = GuideKnowledgeBase.Normalize(slug);
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            return null;
        }

        return models.FirstOrDefault(model => GuideKnowledgeBase.Normalize(model.Id.Entry) == normalizedSlug);
    }

    private static string ReplacePlaceholders(string text, IReadOnlyDictionary<string, object?>? variables, TextLanguage languageHint)
    {
        var current = text;
        for (var pass = 0; pass < 6; pass++)
        {
            var replacedAny = false;
            var next = PlaceholderRegex.Replace(current, match =>
            {
                replacedAny = true;
                return ResolvePlaceholder(match.Groups[1].Value, variables, languageHint);
            });

            current = next;
            if (!replacedAny || !PlaceholderRegex.IsMatch(current))
            {
                break;
            }
        }

        return current;
    }

    private static string ResolvePlaceholder(string expression, IReadOnlyDictionary<string, object?>? variables, TextLanguage languageHint)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return string.Empty;
        }

        var segments = expression.Split(':');
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        var variablePath = segments[0].Trim();
        var value = ResolveValue(variablePath, variables, languageHint);
        object? current = value;

        for (var index = 1; index < segments.Length; index++)
        {
            var formatter = segments[index].Trim();
            if (string.IsNullOrWhiteSpace(formatter))
            {
                continue;
            }

            if (formatter.Equals("plural", StringComparison.OrdinalIgnoreCase))
            {
                var options = index + 1 < segments.Length ? segments[++index] : string.Empty;
                current = ApplyPlural(current, options);
                continue;
            }

            if (formatter.Equals("cond", StringComparison.OrdinalIgnoreCase))
            {
                var options = index + 1 < segments.Length ? segments[++index] : string.Empty;
                current = ApplyConditional(current, options);
                continue;
            }

            current = ApplyInlineFormatter(current, formatter, languageHint);
        }

        return ConvertToDisplayString(current, languageHint);
    }

    private static object? ResolveValue(string variablePath, IReadOnlyDictionary<string, object?>? variables, TextLanguage languageHint)
    {
        if (string.IsNullOrWhiteSpace(variablePath))
        {
            return string.Empty;
        }

        if (variables is not null)
        {
            if (variables.TryGetValue(variablePath, out var exactValue))
            {
                return exactValue;
            }

            var root = variablePath.Split('.')[0];
            if (variables.TryGetValue(root, out var rootValue))
            {
                return rootValue;
            }
        }

        return variablePath.Split('.')[0] switch
        {
            "energyPrefix" => "⚡",
            "singleStarIcon" => "★",
            "ApplierName" => languageHint == TextLanguage.Chinese ? "施加者" : "source",
            "TargetName" => languageHint == TextLanguage.Chinese ? "目标" : "target",
            "OwnerName" => languageHint == TextLanguage.Chinese ? "持有者" : "owner",
            _ => string.Empty
        };
    }

    private static object ApplyInlineFormatter(object? value, string formatter, TextLanguage languageHint)
    {
        if (formatter.StartsWith("energyIcons", StringComparison.OrdinalIgnoreCase))
        {
            return FormatEnergyIcons(value, formatter);
        }

        if (formatter.StartsWith("starIcons", StringComparison.OrdinalIgnoreCase))
        {
            return FormatStarIcons(value, formatter);
        }

        if (formatter.StartsWith("percentMore", StringComparison.OrdinalIgnoreCase) || formatter.StartsWith("percent", StringComparison.OrdinalIgnoreCase))
        {
            var number = GetNumericValue(value);
            return number.HasValue ? $"{number.Value.ToString(CultureInfo.InvariantCulture)}%" : string.Empty;
        }

        if (formatter.StartsWith("diff", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertToDisplayString(value, languageHint);
        }

        return ConvertToDisplayString(value, languageHint);
    }

    private static string ApplyPlural(object? value, string options)
    {
        var choices = options.Split('|');
        if (choices.Length == 0)
        {
            return string.Empty;
        }

        var number = GetNumericValue(value) ?? 0m;
        if (number == 1m)
        {
            return choices[0];
        }

        return choices.Length > 1 ? choices[1] : choices[0];
    }

    private static string ApplyConditional(object? value, string options)
    {
        var choices = options.Split('|');
        if (choices.Length == 0)
        {
            return string.Empty;
        }

        var truthy = value switch
        {
            bool flag => flag,
            decimal number => number != 0,
            int number => number != 0,
            DynamicVar dynamicVar => dynamicVar.IntValue != 0,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => value is not null
        };

        return truthy ? choices[0] : choices.Length > 1 ? choices[1] : string.Empty;
    }

    private static string FormatEnergyIcons(object? value, string formatter)
    {
        var count = GetExplicitFormatterArgument(formatter) ?? GetNumericValue(value) ?? 0m;
        var rounded = Math.Max(0, (int)count);
        return rounded <= 0 ? string.Empty : $"{rounded}⚡";
    }

    private static string FormatStarIcons(object? value, string formatter)
    {
        var count = GetExplicitFormatterArgument(formatter) ?? GetNumericValue(value) ?? 0m;
        var rounded = Math.Max(0, (int)count);
        if (rounded <= 0)
        {
            return string.Empty;
        }

        return rounded <= 4 ? new string('★', rounded) : $"{rounded}★";
    }

    private static decimal? GetExplicitFormatterArgument(string formatter)
    {
        var start = formatter.IndexOf('(');
        var end = formatter.IndexOf(')');
        if (start < 0 || end <= start + 1)
        {
            return null;
        }

        return decimal.TryParse(formatter[(start + 1)..end], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static decimal? GetNumericValue(object? value)
    {
        return value switch
        {
            null => null,
            decimal decimalValue => decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            double doubleValue => (decimal)doubleValue,
            float floatValue => (decimal)floatValue,
            DynamicVar dynamicVar => dynamicVar.IntValue,
            _ when decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static string ConvertToDisplayString(object? value, TextLanguage languageHint)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            DynamicVar dynamicVar => dynamicVar.IntValue.ToString(CultureInfo.InvariantCulture),
            bool flag => flag ? (languageHint == TextLanguage.Chinese ? "是" : "true") : (languageHint == TextLanguage.Chinese ? "否" : "false"),
            IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string SanitizeRichText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sanitized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        sanitized = ImageTagRegex.Replace(sanitized, match => ConvertImageTag(match.Groups[1].Value));
        sanitized = sanitized.Replace("[br]", "\n", StringComparison.OrdinalIgnoreCase);
        sanitized = RichTagRegex.Replace(sanitized, string.Empty);

        var rawLines = sanitized.Split('\n');
        var lines = rawLines
            .Select(line => WhitespaceRegex.Replace(line, " ").Trim())
            .Where((line, index) => line.Length > 0 || index < rawLines.Length - 1)
            .ToList();

        return string.Join("\n", lines).Trim();
    }

    private static string ConvertImageTag(string imagePath)
    {
        if (imagePath.Contains("star_icon", StringComparison.OrdinalIgnoreCase))
        {
            return "★";
        }

        if (imagePath.Contains("energy", StringComparison.OrdinalIgnoreCase) || imagePath.Contains("orb", StringComparison.OrdinalIgnoreCase))
        {
            return "⚡";
        }

        return string.Empty;
    }

    private static bool LooksUnresolved(string text)
    {
        return text.Contains('{') || text.Contains('}') || text.Contains("[img]", StringComparison.OrdinalIgnoreCase);
    }

    private static TextLanguage DetectLanguage(string text)
    {
        foreach (var character in text)
        {
            if (character >= 0x4E00 && character <= 0x9FFF)
            {
                return TextLanguage.Chinese;
            }
        }

        return TextLanguage.Other;
    }

    private enum TextLanguage
    {
        Other,
        Chinese
    }
}