using System.IO.Compression;
using UsfmIntegrityStudio.Models;

internal static class CleanerAuditTests
{
    internal static void CheckResult(UsfmCleanResult result, List<string> failures)
    {
        var fullStops = result.Findings.Where(f => f.Type == "Missing space after full stop").ToArray();
        if (fullStops.Length != 2 || !fullStops.Any(f => f.Reference.Contains("footnote")))
            failures.Add("Audit full-stop count/footnote reference mismatch");
        if (result.Findings.Count(f => f.Type == "Missing space after comma") != 1
            || result.Findings.Count(f => f.Status == "Detected but unchanged") != 1)
            failures.Add("Audit comma/review count mismatch");
        var summary = string.Join("\n", CleanerSpacingAudit.Summary(result));
        if (summary.Contains("Kalasha") || !summary.Contains("Full-Stop Spacing (spaces added): 2"))
            failures.Add("Project-specific audit summary mismatch");
        var txt = File.ReadAllText(result.ReportPath);
        if (!txt.Contains("Not checked:") || !txt.Contains("Detected but unchanged") || !txt.Contains("Before:") || !txt.Contains("After:"))
            failures.Add("TXT audit detail/coverage missing");
        var again = UsfmProjectCleanerService.Clean(result.OutputPath, result.OutputPath + ".second.usfm");
        if (again.FilesChanged != 0 || again.Findings.Any(f => f.Status == "Fixed"))
            failures.Add("Audit reports fixed changes on unchanged second pass");
    }

    internal static void Run(List<string> failures)
    {
        var markerAudit = new CleanerAuditTrail { Path = "project/03/01.txt" };
        markerAudit.Capture("Literal chapter marker removal", "\\c 3 \\v 1 Text", "\\v 1 Text");
        if (markerAudit.Findings.Count != 1 || markerAudit.Findings[0].Original != "\\c 3 "
            || markerAudit.Findings[0].Replacement != "")
            failures.Add("Chapter-marker audit fragmented the verified removed token");
        var multipleMarkers = new CleanerAuditTrail { Path = "project/03/01.txt" };
        multipleMarkers.Capture("Literal chapter marker removal", "\\c 3 \\v 1 A \\c 4 \\v 1 B", "\\v 1 A \\v 1 B");
        if (multipleMarkers.Findings.Count != 2
            || multipleMarkers.Findings[0].Original != "\\c 3 " || multipleMarkers.Findings[1].Original != "\\c 4 ")
            failures.Add("Repeated complete chapter-marker audit failed");
        var audit = new CleanerAuditTrail { Path = "project/01/01.txt" };
        var before = "\\v 1 لفظ۔اگلا لفظ۔اگلا \\f + \\ft مثال،اگلا \\f*";
        var after = CleanerSpacingAudit.Normalize(before);
        audit.Capture("Punctuation/spacing normalization", before, after);
        if (audit.Findings.Count(f => f.Type == "Missing space after full stop") != 2
            || audit.Findings.Count(f => f.Type == "Missing space after comma") != 1)
            failures.Add("Repeated identical excerpts lost in stage audit");
        audit.Capture("Quote spacing", "\\v 1 لفظ\"سلام\"", "\\v 1 لفظ”سلام“");
        if (audit.Findings.Count(f => f.Type == "Straight double quote conversion") != 2)
            failures.Add("Quote conversions not split into audit entries");
        var wordChanges = new List<UrduWordCorrection>();
        UrduWordNormalizer.Normalize("\\v 1 متن \\f + \\ft ہے \\f*", "ur", "project/01/01.txt", wordChanges);
        if (wordChanges.Count != 1 || !wordChanges[0].Location.Contains("footnote") || !wordChanges[0].Location.Contains("character"))
            failures.Add("Urdu correction footnote location missing");
        var root = Path.Combine(Path.GetTempPath(), "uis-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "MAT.usfm");
            File.WriteAllText(input, "\\id MAT\n\\c 1\n\\v 1 لفظ۔اگلا لفظ،اگلا \"سلام\" <script>bad</script>\u0001 ختم۔۔\n");
            var result = UsfmProjectCleanerService.Clean(input, Path.Combine(root, "clean.usfm"));
            if (!result.Findings.Any(f => f.Type == "Unsafe control removal" && f.Original == "\u0001")
                || !result.Findings.Any(f => f.Type == "Straight double quote conversion"))
                failures.Add("Control/quote audit entries missing");
            var html = File.ReadAllText(Path.ChangeExtension(result.ReportPath, ".html"));
            if (html.Contains("<script>") || !html.Contains("U+0001") || html.Contains("<script src") || html.Contains("<link"))
                failures.Add("Audit HTML escaping/offline safety failed");
            if (!html.Contains("class='count'") || !html.Contains("<th scope='row'>Files Scanned</th>")
                || !html.Contains("Technical Repairs") || !html.Contains("Exact Edit")
                || !html.Contains("white-space:pre;") || !html.Contains("overflow-x:auto")
                || html.Contains("Full cleaner report") || html.Contains("Legacy punctuation/spacing normalization passes"))
                failures.Add("Compact HTML layout or nonduplicated summary failed");
            var activity = string.Join("\n", CleanerSpacingAudit.ActivitySummary(result));
            if (activity.Contains("legacy", StringComparison.OrdinalIgnoreCase) || activity.Contains("stage", StringComparison.OrdinalIgnoreCase)
                || !activity.Contains("Files Changed") || activity.Split('\n').Length != 3)
                failures.Add("Activity log is not compact");
            var sorted = result with { Findings = new[]
            {
                new CleanerFinding("Fixed", "Quote spacing", "Quote spacing", "project/10/01.txt", "10:1", 1, "A", "B", "chapter ten", "ten fixed"),
                new CleanerFinding("Fixed", "Quote spacing", "Quote spacing", "project/02/03.txt", "2:3", 1, "A", "B", "chapter two · b`hi", "two fixed")
            } };
            CleanerSpacingAudit.Write(sorted);
            html = File.ReadAllText(Path.ChangeExtension(result.ReportPath, ".html"));
            if (html.IndexOf("chapter two", StringComparison.Ordinal) >= html.IndexOf("chapter ten", StringComparison.Ordinal)
                || !html.Contains("No flags from the implemented checks."))
                failures.Add("Report numeric ordering or honest empty review failed");
            if (!System.Net.WebUtility.HtmlDecode(html).Contains("chapter two · b`hi") || !html.Contains("class='excerpt' dir='auto'"))
                failures.Add("Readable report altered literal middle dots, accents or RTL direction");
            var controls = new CleanerAuditTrail { Path = "project/09/30.txt" };
            var controlBefore = "\\v 31 Text " + new string('\u202c', 18);
            controls.Capture("Bidi control removal", controlBefore, "\\v 31 Text ");
            var controlRow = controls.Findings.Single();
            var quantity = CleanerSpacingAudit.Quantities(controlRow).Single();
            if (quantity.Count != 18 || quantity.Unit != "characters removed" || controlRow.Column != 12 || controlRow.Reference != "09:31")
                failures.Add("Exact control count/location reporting failed");
            var spaces = new CleanerFinding("Fixed", "Repeated inline spaces", "Spacing", "project/01/01.txt", "1:1", 1, "   ", "", "A    B", "A B");
            if (CleanerSpacingAudit.Quantities(spaces).Single() != ("spaces removed", 3)
                || CleanerSpacingAudit.Quantities(controlRow with { Type = "Unicode BOM removal", Original = "\ufeff\ufeff" }).Single().Count != 2)
                failures.Add("Exact repeated-space/BOM character counts failed");
            var unicode = new CleanerAuditTrail { Path = "project/01/01.txt" };
            unicode.Capture("Unsafe control removal", "\\v 1 😀x\u0001\u0002", "\\v 1 😀x");
            if (unicode.Findings.Single().Column != 8 || CleanerSpacingAudit.Quantities(unicode.Findings.Single()).Single().Count != 2)
                failures.Add("Unicode character positions/counts use UTF-16 units");
            var exact = result with { Findings = controls.Findings.ToArray() };
            CleanerSpacingAudit.Write(exact);
            html = File.ReadAllText(Path.ChangeExtension(result.ReportPath, ".html"));
            var exactTxt = File.ReadAllText(result.ReportPath);
            if (!html.Contains("Bidi Controls (characters removed)</th><td class='count'>18</td>")
                || !html.Contains("18 characters removed") || !html.Contains("character 12")
                || !exactTxt.Contains("Bidi Controls (characters removed): 18")
                || html.Contains("Edit Spans") || html.Contains("recorded edit span"))
                failures.Add("HTML/TXT exact count transparency mismatch");
            var collapsible = new[] { "Changes", "Punctuation &amp; Spacing", "Technical Repairs", "Urdu Word Corrections", "Needs Review", "Validation", "Checks &amp; Limits", "Input &amp; Output" };
            if (collapsible.Any(title => !html.Contains("<summary>" + title + "</summary>"))
                || html.Contains("<summary>Summary</summary>") || !html.Contains("<h2>Summary</h2>")
                || System.Text.RegularExpressions.Regex.Matches(html, "<details(?: |>)").Count != System.Text.RegularExpressions.Regex.Matches(html, "</details>").Count)
                failures.Add("Collapsible sections or always-visible summary failed");
            var combined = exact with { Findings = new[] { controlRow with { Type = "Combined stage edit (not individually split)" } } };
            CleanerSpacingAudit.Write(combined);
            html = File.ReadAllText(Path.ChangeExtension(result.ReportPath, ".html"));
            if (!html.Contains("Exact count unavailable for this combined region") || CleanerSpacingAudit.Quantities(combined.Findings[0]).Count != 0)
                failures.Add("Large combined region silently presented as one correction");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("Cleaner audit tests: repeated occurrences, footnotes, quote/control entries, review status, offline escaping and idempotence.");
    }
}
