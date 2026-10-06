using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace UsfmIntegrityStudio.Models;

internal static class CleanerSpacingAudit
{
    // Automatic cleanup rules are unchanged; reporting never feeds text back into them.
    private static readonly Regex Join = new(@"(?<=[\p{L}\p{M}])[۔،](?=\p{L})", RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"(?<=\S)[ \u00a0]{2,}(?=\S)", RegexOptions.CultureInvariant);
    internal static string Normalize(string text)
    {
        var result = Join.Replace(text.Replace('\u00a0', ' '), "$0 ");
        return Spaces.Replace(result, " ");
    }
    internal static string HtmlPath(string path) => Path.ChangeExtension(path, ".html");

    internal static IReadOnlyList<string> ActivitySummary(UsfmCleanResult result) => new[]
    {
        $"Files Changed: {result.FilesChanged} / {result.FilesScanned}",
        $"Review Flags: {result.Findings.Count(f => f.Status == "Detected but unchanged")}",
        $"Validation Issues: {result.VerificationIssueCount}"
    };

    private static string ShortLabel(string label) => label switch
    {
        "Missing space after full stop" => "Full-Stop Spacing",
        "Missing space after comma" => "Comma Spacing",
        "Missing space after question/exclamation" => "Question / Exclamation Spacing",
        "Non-breaking space normalization" => "Nonbreaking Spaces",
        "Repeated inline spaces" => "Repeated Spaces",
        "Space before punctuation" => "Spaces Before Punctuation",
        "Whitespace normalization" => "Whitespace",
        "Literal chapter marker removal" => "Chapter Markers",
        "Anchored verse-line normalization" => "Verse Lines",
        "Bidi control removal" => "Bidi Controls",
        "Unsafe control removal" => "Unsafe Controls",
        "Generator metadata stamp" => "Generator Stamp",
        "Finder metadata file removal" => "Finder Metadata",
        "Quote spacing" => "Quote Spacing",
        "Parenthesis spacing" => "Parenthesis Spacing",
        "Punctuation/spacing normalization" => "Spacing",
        "Chunk punctuation/spacing normalization" => "Chunk Spacing",
        "Chapter title spacing" => "Title Spacing",
        "Chapter/title marker cleanup" => "Title Markers",
        "Duplicate visible verse-marker removal" => "Duplicate Verse Markers",
        "Visible verse-marker normalization" => "Verse Markers",
        "Stray leading verse marker removal" => "Stray Verse Markers",
        "Verse-marker word-joiner residue removal" => "Marker Residue",
        "Unicode BOM removal" => "BOM Removal",
        "Straight quote conversion" => "Straight Quotes",
        "Straight double quote conversion" => "Double Quotes",
        "Straight single quote conversion" => "Single Quotes",
        "Directional quote repair" => "Quote Repair",
        "Directional quote normalization" => "Quote Normalization",
        "Unpaired quote closer repair" => "Unpaired Quotes",
        "Source-checked direct speech repair" => "Source Speech Repair",
        "Kalasha accent normalization" => "Kalasha Accents",
        "Repeated punctuation" => "Repeated Punctuation",
        "Post-clean review" => "Review",
        _ => label
    };

    private static bool IsTechnical(CleanerFinding finding) =>
        finding.Line == 0 || Regex.IsMatch(finding.Stage, "marker|control|metadata|residue|verse-line|BOM", RegexOptions.IgnoreCase);

    private static int Number(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : int.MaxValue;
    }

    private static IEnumerable<CleanerFinding> Ordered(IEnumerable<CleanerFinding> findings) => findings
        .OrderBy(f => f.Path.Split('/')[0], StringComparer.Ordinal)
        .ThenBy(f => Number(f.Reference, @"^(\d+):"))
        .ThenBy(f => Number(f.Reference, @":(\d+)"))
        .ThenBy(f => Number(f.Path.Replace('\\', '/'), @"/(\d+)\.txt$"))
        .ThenBy(f => f.Line);

    // Counts are derived from the captured edit, not the number of report rows or cleaner passes.
    internal static IReadOnlyList<(string Unit, int Count)> Quantities(CleanerFinding finding)
    {
        if (finding.Type == "Combined stage edit (not individually split)") return Array.Empty<(string, int)>();
        if (finding.Type == "Finder metadata file removal") return new[] { ("files removed", 1) };
        if (finding.Type == "Generator metadata stamp") return new[] { ("manifests updated", 1) };
        if (finding.Type == "Unicode BOM removal") return new[] { ("characters removed", finding.Original == "U+FEFF" ? 1 : finding.Original.EnumerateRunes().Count()) };
        if (finding.Type == "Literal chapter marker removal") return new[] { ("markers removed", 1) };
        var old = finding.Original.EnumerateRunes().ToArray();
        var next = finding.Replacement.EnumerateRunes().ToArray();
        if (finding.Status != "Fixed") return new[] { ("characters flagged", old.Length) };
        if (old.Length == 0) return new[] { (next.All(Rune.IsWhiteSpace) ? "spaces added" : "characters added", next.Length) };
        if (next.Length == 0) return new[] { (old.All(Rune.IsWhiteSpace) ? "spaces removed" : "characters removed", old.Length) };
        if (old.Length == next.Length) return new[] { ("characters replaced", old.Zip(next).Count(pair => pair.First != pair.Second)) };
        return new[] { ("characters removed", old.Length), ("characters added", next.Length) };
    }

    private static string QuantityText(CleanerFinding finding)
    {
        var quantities = Quantities(finding);
        return quantities.Count == 0 ? "Exact count unavailable for this combined region" : string.Join("; ", quantities.Select(q => $"{q.Count} {q.Unit}"));
    }

    private static IEnumerable<(string Label, string Unit, int Count)> ChangeTotals(UsfmCleanResult result) =>
        result.Findings.Where(f => f.Status == "Fixed")
            .SelectMany(f => Quantities(f).Select(q => (Label: ShortLabel(f.Type), q.Unit, q.Count)))
            .GroupBy(q => (q.Label, q.Unit))
            .OrderBy(g => g.Key.Label, StringComparer.Ordinal).ThenBy(g => g.Key.Unit, StringComparer.Ordinal)
            .Select(g => (g.Key.Label, g.Key.Unit, g.Sum(q => q.Count)));

    internal static IReadOnlyList<string> Summary(UsfmCleanResult result)
    {
        var lines = new List<string>
        {
            $"Files scanned: {result.FilesScanned}", $"Files changed: {result.FilesChanged}",
            $"Affected text lines (stage-local): {result.Findings.Where(f => f.Status == "Fixed" && f.Line > 0).Select(f => (f.Path, f.Line)).Distinct().Count()}",
            $"Human-review findings: {result.Findings.Count(f => f.Status == "Detected but unchanged")}",
            $"Verification issues after cleaning: {result.VerificationIssueCount}"
        };
        foreach (var total in ChangeTotals(result)) lines.Add($"{total.Label} ({total.Unit}): {total.Count}");
        if (result.UrduWordCorrections.Count > 0)
            lines.Add($"Approved Urdu whole-word corrections: {result.UrduWordCorrections.Count} occurrence(s)");
        return lines;

    }

    internal static void Write(UsfmCleanResult result)
    {
        var summary = Summary(result);
        var coverage = result.SourceComparisonProjects.Count == 0
            ? "Not checked: source-based direct-speech comparison; no compatible local source was loaded."
            : "Source-based comparison enabled only for: " + string.Join(", ", result.SourceComparisonProjects);
        var lines = new List<string> { "UIS Premium USFM/Project Cleaner Report", $"Generated: {DateTimeOffset.Now:O}",
            "Input: " + result.InputPath, "Output: " + result.OutputPath };
        lines.AddRange(summary);
        lines.Add("Change counts use the exact unit shown, not report rows or processing passes. Stage-local positions count Unicode characters (one-based).");
        lines.Add(coverage);
        lines.Add("Not checked: general spelling, translation accuracy, and unapproved linguistic/diacritic rules.");
        lines.Add("Before/after excerpts describe the indicated cleaning stage. Later stages can change the same excerpt again.");
        lines.Add("Fixed — individual stage entries:");
        foreach (var finding in result.Findings.Where(f => f.Status == "Fixed")) lines.Add(Detail(finding));
        lines.Add("Approved Urdu whole-word corrections:");
        foreach (var correction in result.UrduWordCorrections)
            lines.Add($"{correction.Location}: {Visible(correction.Original)} → {Visible(correction.Replacement)}");
        lines.Add("Detected but unchanged — human review:");
        foreach (var finding in result.Findings.Where(f => f.Status == "Detected but unchanged")) lines.Add(Detail(finding));
        lines.Add("Structural repairs:");
        lines.AddRange(result.StructuralRepairs.Count > 0 ? result.StructuralRepairs.Select(r => "- " + r) : new[] { "- none" });
        lines.Add("Post-clean verification:");
        lines.AddRange(result.VerificationIssues.Count > 0 ? result.VerificationIssues.Select(r => "- " + r) : new[] { "- passed" });
        lines.Add("Readable offline report: " + HtmlPath(result.ReportPath));
        Directory.CreateDirectory(Path.GetDirectoryName(result.ReportPath) ?? Directory.GetCurrentDirectory());
        File.WriteAllLines(result.ReportPath, lines, new UTF8Encoding(false));

        var html = new StringBuilder("""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>UIS Cleaner Report</title><style>
*{box-sizing:border-box}body{font:17px Georgia,serif;background:#f6f3eb;color:#173f36;margin:0;padding:24px}h1{margin-top:0}h2{margin-top:32px}.muted,small{color:#53665f}small{display:block;font:13px sans-serif;margin-top:6px}.summary-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:24px;max-width:1100px}.metrics{width:100%;border-collapse:collapse}.metrics th,.metrics td{padding:9px 12px;border-bottom:1px solid #d5d9d2}.metrics th{text-align:left;font-weight:normal}.metrics .count{text-align:right;font-variant-numeric:tabular-nums;font-weight:bold;white-space:nowrap;width:5em}.scroll{overflow-x:auto;border:1px solid #c6cec4;border-radius:8px}.findings{width:100%;border-collapse:separate;border-spacing:0;table-layout:auto}.findings th,.findings td{padding:12px;border-bottom:1px solid #d5d9d2;text-align:left;vertical-align:top}.findings th{position:sticky;top:0;background:#173f36;color:#fff;font:600 14px sans-serif}.findings tr:nth-child(even) td{background:#eeeee5}.location{min-width:145px;max-width:220px;overflow-wrap:anywhere}.change{min-width:130px;max-width:210px}.excerpt{width:30%;min-width:340px;white-space:pre;unicode-bidi:plaintext;font-size:19px;line-height:1.7}.exact{min-width:150px;white-space:pre-wrap;unicode-bidi:plaintext;overflow-wrap:anywhere}mark{background:#ffe3a0;color:#263a2e;padding:2px 4px;border-radius:3px}.row-number{color:#65756d;white-space:nowrap}.paths{overflow-wrap:anywhere}.checks{max-width:1100px;border-left:4px solid #b99b57;padding:6px 16px;margin-top:30px}.words .excerpt{min-width:160px}details{margin:18px 0}summary{cursor:pointer;font-weight:bold;padding:12px 0;font-size:21px}.quantity{min-width:140px;font-variant-numeric:tabular-nums}li{margin:8px 0}@media(max-width:600px){body{padding:12px}.excerpt{min-width:300px}.summary-grid{grid-template-columns:1fr}}@media print{.scroll{overflow:visible}.excerpt{white-space:pre-wrap;min-width:0}.findings th{position:static}.findings{font-size:11px}.excerpt{font-size:13px}}
</style></head><body><h1>UIS Cleaner Report</h1>
""");
        var version = typeof(CleanerSpacingAudit).Assembly.GetName().Version;
        html.Append($"<p class='muted'>UIS v{version?.ToString(3)} · {Encode(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm zzz"))} · Offline</p>");
        html.Append($"<details class='paths'><summary>Input &amp; Output</summary><p><strong>Input:</strong> {Encode(result.InputPath)}</p><p><strong>Output:</strong> {Encode(result.OutputPath)}</p></details>");
        html.Append("<div class='summary-grid'><section><h2>Summary</h2><table class='metrics'><tbody>");
        Metric("Files Scanned", result.FilesScanned);
        Metric("Files Changed", result.FilesChanged);
        Metric("Stage-Local Lines", result.Findings.Where(f => f.Status == "Fixed" && f.Line > 0).Select(f => (f.Path, f.Line)).Distinct().Count());
        Metric("Review Flags", result.Findings.Count(f => f.Status == "Detected but unchanged"));
        Metric("Validation Issues", result.VerificationIssueCount);
        Metric("Urdu Word Corrections", result.UrduWordCorrections.Count);
        Metric("Uncounted Combined Regions", result.Findings.Count(f => f.Type == "Combined stage edit (not individually split)"));
        html.Append("</tbody></table></section></div><details open><summary>Changes</summary><table class='metrics'><tbody>");
        foreach (var total in ChangeTotals(result)) Metric($"{total.Label} ({total.Unit})", total.Count);
        if (result.UrduWordCorrections.Count > 0) Metric("Urdu Words (occurrences corrected)", result.UrduWordCorrections.Count);
        html.Append("</tbody></table></details>");
        Section("Punctuation & Spacing", result.Findings.Where(f => f.Status == "Fixed" && !IsTechnical(f)));
        Section("Technical Repairs", result.Findings.Where(f => f.Status == "Fixed" && IsTechnical(f)));
        html.Append("<details open><summary>Urdu Word Corrections</summary>");
        if (result.UrduWordCorrections.Count == 0) html.Append("<p class='muted'>No approved word corrections applied.</p>");
        else
        {
            html.Append("<div class='scroll'><table class='findings words'><thead><tr><th>#</th><th>Location</th><th>Count</th><th>Before</th><th>After</th><th>Exact Edit</th></tr></thead><tbody>");
            var wordRow = 0;
            foreach (var correction in result.UrduWordCorrections
                         .OrderBy(c => Number(c.Location, @"^(\d+):"))
                         .ThenBy(c => Number(c.Location, @":(\d+)"))
                         .ThenBy(c => Number(c.Location, @"/(\d+)\.txt")))
                html.Append($"<tr><td class='row-number'>{++wordRow}</td><td class='location'>{Encode(correction.Location)}</td><td>1 word corrected</td><td class='excerpt' dir='auto'>{Encode(Readable(correction.Original))}</td><td class='excerpt' dir='auto'>{Encode(Readable(correction.Replacement))}</td><td class='exact' dir='auto'><mark>{Encode(Visible(correction.Original))}</mark> → <mark>{Encode(Visible(correction.Replacement))}</mark></td></tr>");
            html.Append("</tbody></table></div>");
        }
        html.Append("</details>");
        Section("Needs Review", result.Findings.Where(f => f.Status == "Detected but unchanged"));
        html.Append("<details open><summary>Validation</summary>");
        if (result.VerificationIssues.Count == 0) html.Append("<p>Passed the implemented integrity checks.</p>");
        else
        {
            html.Append("<ul>");
            foreach (var issue in result.VerificationIssues) html.Append("<li>" + Encode(issue) + "</li>");
            html.Append("</ul>");
        }
        html.Append("</details>");
        if (result.StructuralRepairs.Count > 0)
        {
            html.Append("<details><summary>Structural Notes</summary><ul>");
            foreach (var repair in result.StructuralRepairs) html.Append("<li>" + Encode(repair) + "</li>");
            html.Append("</ul></details>");
        }
        html.Append("<details class='checks'><summary>Checks &amp; Limits</summary><p>" + Encode(coverage) + "</p><p>Counts use the displayed unit: characters, spaces, markers, words or files. They are not row counts or cleaner passes. Different units are not summed. Changes are counted at each cleaning step, not as a final-file net total. Combined regions without an exact count are explicitly marked and excluded from change totals.</p><p>Stage-Local Lines counts distinct file/line pairs across steps, not original-document lines. Line and character positions are one-based and refer to the named step. Before / After excerpts belong to that step; Exact Edit shows spaces as · and invisible controls as Unicode labels.</p><p>General spelling, translation accuracy, and unapproved language rules are not checked. The separate TXT report retains the complete audit. No scripts or external resources are used.</p></details></body></html>");
        File.WriteAllText(HtmlPath(result.ReportPath), html.ToString(), new UTF8Encoding(false));

        void Metric(string label, int count) => html.Append($"<tr><th scope='row'>{Encode(label)}</th><td class='count'>{count}</td></tr>");
        void Section(string title, IEnumerable<CleanerFinding> findings)
        {
            var rows = Ordered(findings).ToArray();
            html.Append("<details open><summary>" + Encode(title) + "</summary>");
            if (rows.Length == 0)
            {
                html.Append("<p class='muted'>" + (title == "Needs Review" ? "No flags from the implemented checks." : "No changes recorded.") + "</p></details>");
                return;
            }
            html.Append("<div class='scroll'><table class='findings'><thead><tr><th>#</th><th>Location</th><th>Change</th><th>Count</th><th>Before</th><th>After</th><th>Exact Edit</th></tr></thead><tbody>");
            var row = 0;
            foreach (var finding in rows)
            {
                var step = finding.Stage == finding.Type ? "" : "<small>" + Encode(ShortLabel(finding.Stage)) + "</small>";
                var position = finding.Line == 0 ? "File-level" : $"Line {finding.Line}" + (finding.Column > 0 ? $", character {finding.Column}" : "");
                html.Append($"<tr><td class='row-number'>{++row}</td><td class='location'>{Encode(finding.Reference)}<small>{Encode(finding.Path)}<br>{Encode(position)}</small></td><td class='change'>{Encode(ShortLabel(finding.Type))}{step}</td><td class='quantity'>{Encode(QuantityText(finding))}</td><td class='excerpt' dir='auto'>{Encode(Readable(finding.BeforeExcerpt))}</td><td class='excerpt' dir='auto'>{Encode(Readable(finding.AfterExcerpt))}</td><td class='exact' dir='auto'><mark>{Encode(Visible(finding.Original))}</mark> → <mark>{Encode(Visible(finding.Replacement))}</mark></td></tr>");
            }
            html.Append("</tbody></table></div></details>");
        }
    }
    private static string Readable(string text) => Visible(text, showSpaces: false);
    private static string Detail(CleanerFinding f) => $"{f.Status} | {f.Type} | {f.Stage} | {f.Reference} | {f.Path}, line {f.Line}, character {f.Column} | {QuantityText(f)} | {Visible(f.Original)} → {Visible(f.Replacement)} | Before: {Visible(f.BeforeExcerpt)} | After: {Visible(f.AfterExcerpt)}";
    private static string Encode(string text) => WebUtility.HtmlEncode(text);
    private static string Visible(string text, bool showSpaces = true)
    {
        if (text.Length == 0) return "[empty]";
        return Regex.Replace((showSpaces ? text.Replace(" ", "·") : text).Replace("\u00a0", "[U+00A0]"), @"[\u0000-\u001f\u007f-\u009f\u200b-\u200f\u202a-\u202e\u2060-\u2069\ufeff]", m => $"[U+{(int)m.Value[0]:X4}]");
    }
}
