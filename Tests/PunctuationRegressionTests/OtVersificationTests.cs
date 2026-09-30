using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using UsfmIntegrityStudio.Models;

internal static class OtVersificationTests
{
    public static void Run(Assembly assembly, List<string> failures)
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ConverterRuntime/versification/bttw-en-US-v1.json")));
        var books = data.RootElement.GetProperty("maxVerses").EnumerateObject().ToArray();
        if (books.Length != 39 || books.Sum(b => b.Value.GetArrayLength()) != 929
            || books.Sum(b => b.Value.EnumerateArray().Sum(v => v.GetInt32())) != 23145)
            failures.Add("OT profile: book/chapter/verse totals changed");
        var scanType = assembly.GetType("UsfmIntegrityStudio.Models.DocxScanService")!;
        var runtime = assembly.GetType("UsfmIntegrityStudio.ConverterRuntime.BundledUsfmContractRuntime")!;
        var validate = runtime.GetMethod("ValidateSelectedBookVersification", BindingFlags.NonPublic | BindingFlags.Static)!;
        var root = Path.Combine(Path.GetTempPath(), "uis-ot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            foreach (var book in books)
            {
                var body = new XElement(w + "body", Paragraph(w, book.Name));
                var lines = new List<string>();
                var chapter = 0;
                foreach (var limit in book.Value.EnumerateArray())
                {
                    chapter++;
                    body.Add(Paragraph(w, $"Chapter {chapter}"));
                    lines.Add($"\\c {chapter}");
                    for (var verse = 1; verse <= limit.GetInt32(); verse++)
                        body.Add(Paragraph(w, $"\\V {verse} Text {book.Name} {chapter}:{verse}."));
                    lines.Add($"\\v {limit.GetInt32()} Endpoint.");
                    validate.Invoke(null, [new[] { $"\\c {chapter}", $"\\v {limit.GetInt32()} Endpoint." }, new HashSet<string> { book.Name }, "protestant-ot"]);
                    try
                    {
                        validate.Invoke(null, [new[] { $"\\c {chapter}", $"\\v {limit.GetInt32() + 1} Invalid." }, new HashSet<string> { book.Name }, "protestant-ot"]);
                        failures.Add($"OT boundary: {book.Name} {chapter} accepted excess verse");
                    }
                    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
                }
                var input = Path.Combine(root, book.Name + ".docx");
                Write(input, new XDocument(new XElement(w + "document", body)));
                var scan = scanType.GetMethod("Scan")!.Invoke(null, [input, CanonProfile.ProtestantOt])!;
                var issues = ((IEnumerable)scan.GetType().GetProperty("Issues")!.GetValue(scan)!).Cast<object>();
                if (issues.Any(i => ((string)i.GetType().GetProperty("Code")!.GetValue(i)!).StartsWith("CANON_VERSE_", StringComparison.Ordinal)))
                    failures.Add($"OT scan: complete {book.Name} has canonical issues");
            }
            var sng = Path.Combine(root, "split.docx");
            Write(sng, new XDocument(new XElement(w + "document", new XElement(w + "body",
                Paragraph(w, "SNG"), Paragraph(w, "Chapter 6"),
                Paragraph(w, "12"), Paragraph(w, "Verse twelve begins"), Paragraph(w, "continuation text"),
                new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "\\")), new XElement(w + "r", new XElement(w + "t", "V 13 Final text.")))))));
            var output = Path.Combine(root, "standardized.docx");
            var second = Path.Combine(root, "second.docx");
            var standardize = scanType.GetMethod("Standardize")!;
            standardize.Invoke(null, [sng, output, CanonProfile.ProtestantOt, new HashSet<string> { "SNG" }, new HashSet<string>(), false]);
            standardize.Invoke(null, [output, second, CanonProfile.ProtestantOt, new HashSet<string> { "SNG" }, new HashSet<string>(), false]);
            if (!ReadText(output).Contains("\\v 13 Final text.") || Read(output) != Read(second))
                failures.Add("OT standardization: split uppercase marker or idempotence failed");
            var conversion = DocxConversionService.Execute(new(sng, Path.Combine(root, "converted"), Path.Combine(root, "report.txt"), "permissive", "protestant-ot", "und", ["SNG"], true));
            if (!conversion.GeneratedOutput || !File.ReadAllText(conversion.GeneratedUsfmPaths.Single()).Contains("\\v 13 Final text.")
                || !File.ReadAllText(conversion.GeneratedUsfmPaths.Single()).Contains("\\v 12 Verse twelve begins continuation text"))
                failures.Add("SNG conversion: 6:13 text anchor was not preserved");
            else
            {
                var package = BttwProjectPackageService.PackageUsfm(conversion.GeneratedUsfmPaths.Single(), "und");
                using var zip = ZipFile.OpenRead(package.TstudioPath);
                if (zip.GetEntry("und_sng_text_reg/06/13.txt") is null) failures.Add("SNG package: missing canonical 06/13.txt");
            }
            // Preserve the existing NT ceiling (3 John has 15 verses in this build).
            validate.Invoke(null, [new[] { "\\c 1", "\\v 15 Existing NT endpoint." }, new HashSet<string> { "3JN" }, "protestant-nt"]);
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("OT tests exercised 39 books, 929 valid and 929 invalid chapter endpoints, complete uppercase-marker scans, split-run standardization, idempotence, SNG conversion/package, and existing NT ceiling.");
    }
    private static XElement Paragraph(XNamespace w, string text) => new(w + "p", new XElement(w + "r", new XElement(w + "t", text)));
    private static void Write(string path, XDocument document)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var stream = zip.CreateEntry("word/document.xml").Open();
        document.Save(stream);
    }
    private static string ReadText(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return string.Join("\n", XDocument.Load(stream).Descendants(w + "p")
            .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value))));
    }
    private static string Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(stream).ToString();
    }
}
