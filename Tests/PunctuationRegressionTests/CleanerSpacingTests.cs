using System.IO.Compression;
using UsfmIntegrityStudio.Models;

internal static class CleanerSpacingTests
{
    internal static void Run(List<string> failures)
    {
        var corrections = new List<UrduWordCorrection>();
        var words = "ہے ہیں لئے لیکن تم ہَے ہَیں لیکِن تُم تمہارا ہےَ xہے ہےx";
        var normalized = UrduWordNormalizer.Normalize(words, "ur", "project/01/01.txt", corrections);
        if (corrections.Count != 5 || !normalized.StartsWith("ہَے ہَیں لیے لیکِن تُم")
            || !normalized.EndsWith("تمہارا ہےَ xہے ہےx")
            || UrduWordNormalizer.Normalize(normalized, "urd", "project/01/01.txt", new()) != normalized)
            failures.Add("Urdu whole-word normalization/boundaries/idempotence failed");
        foreach (var language in new string?[] { null, "kls", "ar", "fa" })
            if (UrduWordNormalizer.Normalize(words, language, "project/01/01.txt", new()) != words)
                failures.Add("Urdu normalization changed a non-Urdu project");
        var expansion = "تجھ تجھے مجھ مجھے خدا خداوند قربانی جس جسے تمہیں تمہارے تمہاری";
        var expectedExpansion = "تُجھ تُجھے مُجھ مُجھے خُدا خُداوَند قُربانی جِس جِسے تُمھیں تُمھارے تُمھاری";
        var expansionChanges = new List<UrduWordCorrection>();
        if (UrduWordNormalizer.Normalize(expansion, "urd", "project/01/01.txt", expansionChanges) != expectedExpansion
            || expansionChanges.Count != 12
            || UrduWordNormalizer.Normalize(expectedExpansion, "ur", "project/01/01.txt", new()) != expectedExpansion)
            failures.Add("Expanded Urdu whole-word mappings/idempotence failed");
        var protectedWords = "خداوندی قربانیوں تجھےَ xمجھے جسےx تمہیں\u200c";
        if (UrduWordNormalizer.Normalize(protectedWords, "ur", "project/01/01.txt", new()) != protectedWords)
            failures.Add("Expanded Urdu word fragments/marks/joiners changed");
        foreach (var language in new string?[] { null, "kls", "ar", "fa" })
            if (UrduWordNormalizer.Normalize(expansion, language, "project/01/01.txt", new()) != expansion)
                failures.Add("Expanded Urdu rule changed non-Urdu text");
        var root = Path.Combine(Path.GetTempPath(), "uis-spacing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var book in new[] { "MAT", "GEN" })
            {
                var input = Path.Combine(root, book + ".usfm");
                var output = Path.Combine(root, book + "-cleaned.usfm");
                var second = Path.Combine(root, book + "-second.usfm");
                var source = "\\id " + book + "\n\\c 1\n\\v 1 لفظ۔اگلا لفظ،اگلا ایک  لفظ ایک\u00a0 لفظ ختم۔۔ برقرار... <script>alert(1)</script>\n\\v 2 متن \\f + \\ft نوٹ۔اگلا \\f*\n\\v 3 Latin 3.14 https://example.org b`hi ہے ہیں لئے لیکن تم " + expansion + "\n";
                File.WriteAllText(input, source);
                var result = UsfmProjectCleanerService.Clean(input, output);
                CleanerAuditTests.CheckResult(result, failures);
                var text = File.ReadAllText(output);
                UsfmProjectCleanerService.Clean(output, second);
                if (!text.Contains("لفظ۔ اگلا لفظ، اگلا ایک لفظ") || !text.Contains("نوٹ۔ اگلا")
                    || !text.Contains("ختم۔۔") || !text.Contains("برقرار...") || !text.Contains("3.14 https://example.org b`hi"))
                    failures.Add(book + " cleaner spacing or protected punctuation failed");
                if (text != File.ReadAllText(second) || source != File.ReadAllText(input))
                    failures.Add(book + " cleaner spacing idempotence/input preservation failed");
                var package = BttwProjectPackageService.PackageUsfm(input, "ur");
                var cleanPackage = Path.Combine(root, book + "-cleaned.tstudio");
                var projectResult = UsfmProjectCleanerService.Clean(package.TstudioPath, cleanPackage,
                    book == "MAT" ? CanonProfile.ProtestantNt : CanonProfile.ProtestantOt);
                if (projectResult.UrduWordCorrections.Count != 17 || !File.ReadAllText(Path.ChangeExtension(projectResult.ReportPath, ".html")).Contains("Urdu Word Corrections"))
                    failures.Add(book + " Urdu project normalization/report count failed");
                using (var zip = ZipFile.OpenRead(cleanPackage))
                {
                    var chunk = zip.Entries.First(e => e.FullName.EndsWith("/01/01.txt"));
                    using var reader = new StreamReader(chunk.Open());
                    if (!reader.ReadToEnd().Contains("لفظ۔ اگلا لفظ، اگلا"))
                        failures.Add(book + " project package spacing failed");
                }
                using (var firstZip = ZipFile.OpenRead(cleanPackage))
                {
                    foreach (var entry in firstZip.Entries.Where(e => e.FullName.EndsWith("manifest.json")))
                    {
                        using var manifestReader = new StreamReader(entry.Open());
                        using var manifest = System.Text.Json.JsonDocument.Parse(manifestReader.ReadToEnd());
                        var generator = manifest.RootElement.GetProperty("generator");
                        if (generator.GetProperty("name").GetString() != "ts-desktop" || generator.GetProperty("build").GetString() != "1074")
                            failures.Add(book + " cleaned manifest generator differs from DOCX Import 1074");
                    }
                }
                var secondPackage = Path.Combine(root, book + "-second.tstudio");
                var secondResult = UsfmProjectCleanerService.Clean(cleanPackage, secondPackage,
                    book == "MAT" ? CanonProfile.ProtestantNt : CanonProfile.ProtestantOt);
                using (var a = ZipFile.OpenRead(cleanPackage))
                using (var b = ZipFile.OpenRead(secondPackage))
                {
                    if (!a.Entries.Select(e => e.FullName).Order().SequenceEqual(b.Entries.Select(e => e.FullName).Order()))
                        failures.Add(book + " second clean package paths differ");
                    foreach (var entry in a.Entries)
                    {
                        using var ar = new StreamReader(entry.Open());
                        using var br = new StreamReader(b.GetEntry(entry.FullName)!.Open());
                        if (ar.ReadToEnd() != br.ReadToEnd()) failures.Add(book + " second clean changed entry " + entry.FullName);
                    }
                }
                if (secondResult.Findings.Any(f => f.Type == "Generator metadata stamp"))
                    failures.Add(book + " restamped already-current generator");
                var html = File.ReadAllText(Path.ChangeExtension(result.ReportPath, ".html"));
                if (!html.Contains("<th>Before</th>") || !html.Contains("<th>After</th>") || !html.Contains("Full-Stop Spacing") || !html.Contains("Comma Spacing") || !html.Contains("Repeated Spaces") || !html.Contains("Needs Review") || !html.Contains("Punctuation &amp; Spacing") || html.Contains("<script>alert"))
                    failures.Add(book + " offline report escaping/findings failed");
            }
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("Cleaner spacing/audit tests: Urdu boundaries, NT/OT reports, footnotes, repeated dots, decimals/URLs, input preservation, idempotence, and escaped offline HTML.");
    }
}
