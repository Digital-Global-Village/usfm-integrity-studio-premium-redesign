using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UsfmIntegrityStudio.Models;

public sealed record CleanerFinding(string Status, string Type, string Stage, string Path,
    string Reference, int Line, string Original, string Replacement, string BeforeExcerpt, string AfterExcerpt)
{
    public int Column { get; init; }
}

// Observes existing transformations only; never supplies text back to a cleaner.
internal sealed class CleanerAuditTrail
{
    internal string Path { get; set; } = "text";
    internal List<CleanerFinding> Findings { get; } = new();

    internal void Capture(string stage, string before, string after, string? context = null, int offset = 0)
    {
        if (before == after) return;
        // For a line-level stage, use that stage's actual text after the unchanged document prefix.
        var source = context is null ? before : context[..Math.Min(offset, context.Length)] + before;
        var oldLines = before.Split('\n');
        var newLines = after.Split('\n');
        if (oldLines.Length == newLines.Length)
        {
            var lineOffset = 0;
            for (var i = 0; i < oldLines.Length; i++)
            {
                CaptureLine(stage, oldLines[i], newLines[i], source, offset + lineOffset);
                lineOffset += oldLines[i].Length + 1;
            }
        }
        else CaptureLine(stage, before, after, source, offset);
    }

    private void CaptureLine(string stage, string before, string after, string context, int offset)
    {
        if (before == after) return;
        // Prefer complete verified marker deletions over character-aligned diffs.
        if (stage == "Literal chapter marker removal")
        {
            var markers = Regex.Matches(before, @"(?<!\S)\\c\s+\d+\s*");
            if (markers.Count > 0 && Regex.Replace(before, @"(?<!\S)\\c\s+\d+\s*", "") == after)
            {
                var removed = 0;
                foreach (Match marker in markers)
                {
                    Add(stage, stage, before, after, marker.Index, marker.Length,
                        marker.Index - removed, 0, context, offset);
                    removed += marker.Length;
                }
                return;
            }
        }
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        var oldEnd = before.Length;
        var newEnd = after.Length;
        while (oldEnd > prefix && newEnd > prefix && before[oldEnd - 1] == after[newEnd - 1]) { oldEnd--; newEnd--; }
        var a = before[prefix..oldEnd];
        var b = after[prefix..newEnd];
        // Bound reporting work independently of scripture size. A grouped row is explicitly labeled.
        if ((long)(a.Length + 1) * (b.Length + 1) > 1_000_000)
        {
            Add(stage, "Combined stage edit (not individually split)", before, after, prefix, a.Length, prefix, b.Length, context, offset);
            return;
        }
        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lengths[i, j] = a[i] == b[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var x = 0; var y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y]) { x++; y++; continue; }
            var startX = x; var startY = y;
            while (x < a.Length || y < b.Length)
            {
                if (x < a.Length && y < b.Length && a[x] == b[y]) break;
                if (y < b.Length && (x == a.Length || lengths[x, y + 1] > lengths[x + 1, y])) y++;
                else x++;
            }
            Add(stage, Classify(stage, before, after, prefix + startX, x - startX, prefix + startY, y - startY),
                before, after, prefix + startX, x - startX, prefix + startY, y - startY, context, offset);
        }
    }

    private void Add(string stage, string type, string before, string after, int oldStart, int oldLength,
        int newStart, int newLength, string context, int offset)
    {
        var position = Math.Min(context.Length, offset + oldStart);
        Findings.Add(new("Fixed", type, stage, Path, Reference(Path, context, position),
            1 + context[..position].Count(c => c == '\n'), before.Substring(oldStart, oldLength),
            after.Substring(newStart, newLength), Excerpt(before, oldStart, oldLength), Excerpt(after, newStart, newLength))
        {
            Column = 1 + context[(context[..position].LastIndexOf('\n') + 1)..position].EnumerateRunes().Count()
        });
    }

    private static string Classify(string stage, string before, string after, int start, int length, int nextStart, int nextLength)
    {
        if (!stage.Contains("spacing", StringComparison.OrdinalIgnoreCase)) return stage;
        var old = before.Substring(start, length);
        var replacement = after.Substring(nextStart, nextLength);
        var neighborhood = before[Math.Max(0, start - 1)..Math.Min(before.Length, start + length + 1)] + replacement;
        if (neighborhood.IndexOfAny(new[] { '"', '\'', '“', '”', '‘', '’' }) >= 0)
        {
            if (old.Contains('"')) return "Straight double quote conversion";
            if (old.Contains('\'')) return "Straight single quote conversion";
            if (old.All(char.IsWhiteSpace) && replacement.All(char.IsWhiteSpace)) return "Quote spacing";
            return "Directional quote normalization";
        }
        if (neighborhood.Contains('(') || neighborhood.Contains(')')) return "Parenthesis spacing";
        if (old.All(char.IsWhiteSpace) && replacement.All(char.IsWhiteSpace))
        {
            if (start > 0 && before[start - 1] == '۔' && length == 0) return "Missing space after full stop";
            if (start > 0 && before[start - 1] == '،' && length == 0) return "Missing space after comma";
            if (old.Contains('\u00a0')) return "Non-breaking space normalization";
            if (length > 0 && start + length < before.Length && ",.;:!?،؛؟۔".Contains(before[start + length]))
                return "Space before punctuation";
            if (start > 0 && "!?؟".Contains(before[start - 1]) && length == 0) return "Missing space after question/exclamation";
            if (old.Length >= 2 || (length > 0 && (start > 0 && before[start - 1] == ' ' || start + length < before.Length && before[start + length] == ' ')))
                return "Repeated inline spaces";
            return "Whitespace normalization";
        }
        if (neighborhood.Contains('۔') || neighborhood.Contains('.')) return "Full-stop punctuation normalization";
        if (neighborhood.Contains('،') || neighborhood.Contains(',')) return "Comma punctuation normalization";
        return "Punctuation/spacing normalization";
    }

    internal void Review(string text)
    {
        foreach (Match match in Regex.Matches(text, @"[۔،]{2,}"))
        {
            var excerpt = Excerpt(text, match.Index, match.Length);
            Findings.Add(new("Detected but unchanged", "Repeated punctuation", "Post-clean review", Path,
                Reference(Path, text, match.Index), 1 + text[..match.Index].Count(c => c == '\n'), match.Value,
                match.Value, excerpt, excerpt)
            {
                Column = 1 + text[(text[..match.Index].LastIndexOf('\n') + 1)..match.Index].EnumerateRunes().Count()
            });
        }
    }

    internal void FileChange(string path, string type, string before, string after) =>
        Findings.Add(new("Fixed", type, "File-level cleanup", path, "file-level", 0, before, after, before, after));

    internal static string Reference(string path, string text, int offset)
    {
        var chapter = Regex.Match("/" + path.Replace('\\', '/'), @"/(\d+)/").Groups[1].Value;
        var chapters = Regex.Matches(text[..offset], @"\\c\s+(\d+)");
        if (chapters.Count > 0) chapter = chapters[^1].Groups[1].Value;
        var verses = Regex.Matches(text[..offset], @"\\v\s+(\d+)");
        var verse = verses.Count > 0 ? verses[^1].Groups[1].Value : "heading";
        var footnotes = Regex.Matches(text[..offset], @"\\f(?:e)?(?:\*|(?=\s))");
        var inFootnote = footnotes.Count > 0 && !footnotes[^1].Value.EndsWith("*", StringComparison.Ordinal);
        return $"{(chapter.Length == 0 ? "unspecified chapter" : chapter)}:{verse}" + (inFootnote ? " (footnote)" : "");
    }
    private static string Excerpt(string text, int start, int length) =>
        text[Math.Max(0, start - 24)..Math.Min(text.Length, start + length + 32)];
}
