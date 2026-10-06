using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PunctuationRegressionTests")]

namespace UsfmIntegrityStudio.Models;

public sealed record UrduWordCorrection(string Location, string Original, string Replacement);

internal static class UrduWordNormalizer
{
    private static readonly Dictionary<string, string> Forms = new(StringComparer.Ordinal)
    {
        ["ہے"] = "ہَے", ["ہیں"] = "ہَیں", ["لئے"] = "لیے",
        ["لیکن"] = "لیکِن", ["تم"] = "تُم",
        ["تجھ"] = "تُجھ", ["تجھے"] = "تُجھے", ["مجھ"] = "مُجھ", ["مجھے"] = "مُجھے",
        ["خدا"] = "خُدا", ["خداوند"] = "خُداوَند", ["قربانی"] = "قُربانی",
        ["جس"] = "جِس", ["جسے"] = "جِسے",
        ["تمہیں"] = "تُمھیں", ["تمہارے"] = "تُمھارے", ["تمہاری"] = "تُمھاری"
    };
    private static readonly Regex Words = new(@"(?<![\p{L}\p{M}\p{N}\p{Pc}\u200c\u200d])(?:ہے|ہیں|لئے|لیکن|تم|تجھ|تجھے|مجھ|مجھے|خدا|خداوند|قربانی|جس|جسے|تمہیں|تمہارے|تمہاری)(?![\p{L}\p{M}\p{N}\p{Pc}\u200c\u200d])", RegexOptions.CultureInvariant);

    internal static string Normalize(string text, string? language, string path, List<UrduWordCorrection> changes)
    {
        var code = language?.Trim().ToLowerInvariant();
        if (code is not ("ur" or "urd")) return text;
        return Words.Replace(text, match =>
        {
            var replacement = Forms[match.Value];
            var prefix = text[..match.Index];
            var line = 1 + prefix.Count(c => c == '\n');
            var column = 1 + prefix[(prefix.LastIndexOf('\n') + 1)..].EnumerateRunes().Count();
            changes.Add(new($"{CleanerAuditTrail.Reference(path, text, match.Index)} — {path}, line {line}, character {column}", match.Value, replacement));
            return replacement;
        });
    }
}
