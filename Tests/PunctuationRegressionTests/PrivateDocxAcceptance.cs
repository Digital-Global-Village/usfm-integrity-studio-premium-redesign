using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UsfmIntegrityStudio.Models;

internal static class PrivateDocxAcceptance
{
    public static void Run(Assembly assembly, List<string> failures)
    {
        var input = Environment.GetEnvironmentVariable("UIS_PRIVATE_DOCX");
        if (string.IsNullOrWhiteSpace(input)) return;
        var root = Path.Combine(Path.GetTempPath(), "uis-private-acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var original = File.ReadAllBytes(input);
        try
        {
            var service = assembly.GetType("UsfmIntegrityStudio.Models.DocxScanService")!;
            var scan = service.GetMethod("Scan")!.Invoke(null, [input, CanonProfile.ProtestantNt])!;
            if ((int)scan.GetType().GetProperty("TotalVerseCount")!.GetValue(scan)! != 433) failures.Add("private acceptance: scan expected 433 verses");
            var output = Path.Combine(root, "standardized.docx");
            var second = Path.Combine(root, "second.docx");
            foreach (var pair in new[] { (input, output), (output, second) })
                service.GetMethod("Standardize")!.Invoke(null, [pair.Item1, pair.Item2, CanonProfile.ProtestantNt, new HashSet<string> { "ROM" }, new HashSet<string>(), false]);
            var before = ReadVerses(input);
            var after = ReadVerses(output);
            // Explicitly allow only the three verified pre-existing redundant-full-stop repairs.
            foreach (var reference in new[] { "3:1", "6:2", "9:14" })
            {
                var index = before.FindIndex(pair => pair.Key == reference);
                if (index < 0 || !(before[index].Value.EndsWith("؟۔", StringComparison.Ordinal)
                    || before[index].Value.EndsWith("?۔", StringComparison.Ordinal)
                    || (reference == "9:14" && before[index].Value.EndsWith("!۔", StringComparison.Ordinal))))
                    failures.Add($"private acceptance: expected known punctuation signature at {reference}");
                else before[index] = new(before[index].Key, before[index].Value[..^1]);
            }
            if (before.Count != 433 || after.Count != 433 || before.Select(x => x.Key).Distinct().Count() != 433)
                failures.Add("private acceptance: verse identity count differs");
            if (!before.SequenceEqual(after))
            {
                failures.Add("private acceptance: verse text or anchors changed beyond whitespace");
                foreach (var pair in before.Zip(after).Where(x => !x.First.Equals(x.Second)))
                {
                    var a = pair.First.Value; var b = pair.Second.Value;
                    var i = 0; while (i < Math.Min(a.Length, b.Length) && a[i] == b[i]) i++;
                    Console.WriteLine($"Difference {pair.First.Key}/{pair.Second.Key}: lengths {a.Length}/{b.Length}; offset {i}; before codes {string.Join(",", a.Skip(i).Take(8).Select(c => ((int)c).ToString("X4")))}; after codes {string.Join(",", b.Skip(i).Take(8).Select(c => ((int)c).ToString("X4")))}");
                }
            }
            if (!ReadXml(output).ToString().Equals(ReadXml(second).ToString(), StringComparison.Ordinal))
                failures.Add("private acceptance: second standardization pass changed document XML");
            var conversion = DocxConversionService.Execute(new(output, root, Path.Combine(root, "report.txt"), "permissive", "protestant-nt", "urd", ["ROM"], true));
            if (conversion.GeneratedUsfmPaths.Count != 1) failures.Add("private acceptance: expected one USFM");
            else
            {
                var text = File.ReadAllText(conversion.GeneratedUsfmPaths[0]);
                var verses = new List<KeyValuePair<string, string>>();
                var chapter = "";
                foreach (var line in text.Split('\n'))
                {
                    var cm = Regex.Match(line, @"^\\c\s+(\d+)");
                    if (cm.Success) chapter = cm.Groups[1].Value;
                    var vm = Regex.Match(line, @"^\\v\s+(\d+)\s+(.*)");
                    if (vm.Success) verses.Add(new(chapter + ":" + vm.Groups[1].Value, Compact(vm.Groups[2].Value)));
                }
                if (!before.SequenceEqual(verses))
                {
                    failures.Add("private acceptance: USFM verse anchors or text differ");
                    foreach (var pair in before.Zip(verses).Where(x => !x.First.Equals(x.Second)).Take(10))
                    {
                        var a = pair.First.Value;
                        var b = pair.Second.Value;
                        var i = 0;
                        while (i < Math.Min(a.Length, b.Length) && a[i] == b[i]) i++;
                        Console.WriteLine(
                            $"USFM difference {pair.First.Key}/{pair.Second.Key}: lengths {a.Length}/{b.Length}; offset {i}; " +
                            $"before codes {string.Join(",", a.Skip(i).Take(8).Select(c => ((int)c).ToString("X4")))}; " +
                            $"after codes {string.Join(",", b.Skip(i).Take(8).Select(c => ((int)c).ToString("X4")))}");
                    }
                }
                var package = BttwProjectPackageService.PackageUsfm(conversion.GeneratedUsfmPaths[0], "urd");
                if (package.ChapterCount != 16 || package.ProjectId != "ROM") failures.Add("private acceptance: package book/chapter identity differs");
            }
            if (!original.SequenceEqual(File.ReadAllBytes(input))) failures.Add("private acceptance: original DOCX changed");
            Console.WriteLine("Private acceptance exercised ROM scan, 433 verse text anchors, XML idempotence, USFM conversion and 16-chapter packaging; temporary output removed.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static XDocument ReadXml(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(stream);
    }
    private static string Compact(string text) => Regex.Replace(text, @"\s+", "");
    private static List<KeyValuePair<string, string>> ReadVerses(string path)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var result = new List<KeyValuePair<string, string>>();
        var chapter = "";
        foreach (var paragraph in ReadXml(path).Descendants(w + "p"))
        {
            var text = string.Concat(paragraph.Descendants().Select(x => x.Name == w + "t" ? x.Value : x.Name == w + "br" ? "\n" : ""));
            foreach (var line in text.Split('\n'))
            {
                var normalized = string.Concat(line.Select(c => c >= '۰' && c <= '۹' ? (char)('0' + c - '۰') : c));
                var cm = Regex.Match(normalized.Trim(), @"^باب\s*(\d+)$");
                if (cm.Success) { chapter = cm.Groups[1].Value; continue; }
                var vm = Regex.Match(normalized.Trim(), @"^(\d+)(.*)$");
                if (vm.Success && chapter.Length > 0)
                {
                    result.Add(new(chapter + ":" + vm.Groups[1].Value, Compact(vm.Groups[2].Value)));
                }
                else if (chapter.Length > 0 && result.Count > 0 && !string.IsNullOrWhiteSpace(normalized))
                {
                    // The converter preserves an unnumbered DOCX paragraph as continuation of the preceding verse.
                    var previous = result[^1];
                    result[^1] = new(previous.Key, previous.Value + Compact(normalized));
                }
            }
        }
        return result;
    }
}
