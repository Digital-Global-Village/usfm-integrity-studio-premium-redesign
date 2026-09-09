using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using UsfmIntegrityStudio.Models;

internal static class DocxDetectionTests
{
    public static void Run(Assembly assembly, List<string> failures)
    {
        var type = assembly.GetType("UsfmIntegrityStudio.Models.DocxScanService")!;
        var resolve = type.GetMethod("TryResolveCanonicalBookTitle", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var profile = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ConverterRuntime", "language-profiles", "global-starter.json")));
        var nt = new HashSet<string>("MAT MRK LUK JHN ACT ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM HEB JAS 1PE 2PE 1JN 2JN 3JN JUD REV".Split(' '));
        var checkedHeadings = 0;
        foreach (var book in profile.RootElement.GetProperty("bookAliases").EnumerateObject())
        {
            foreach (var heading in book.Value.EnumerateArray().Select(x => x.GetString()!).Append(book.Name))
            {
                object?[] args = [heading, nt.Contains(book.Name) ? CanonProfile.ProtestantNt : CanonProfile.ProtestantOt, null];
                if (!(bool)resolve.Invoke(null, args)! || !Equals(args[2], book.Name))
                    failures.Add($"heading recognition: {book.Name} [{heading}]");
                checkedHeadings++;
            }
        }
        foreach (var sentence in new[] { "We read Romans today.", "Romans tells us about faith", "1 Thessalonians teaches us about hope today." })
        {
            object?[] args = [sentence, CanonProfile.ProtestantNt, null];
            if ((bool)resolve.Invoke(null, args)!) failures.Add($"false book heading: {sentence}");
        }
        var root = Path.Combine(Path.GetTempPath(), "uis-detection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var input = Path.Combine(root, "input.docx");
            using (var zip = ZipFile.Open(input, ZipArchiveMode.Create))
            using (var stream = zip.CreateEntry("word/document.xml").Open())
                new XDocument(new XElement(w + "document", new XElement(w + "body",
                    new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "رومیوں"))),
                    new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "باب ۱"))),
                    new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "1پس ایک۔"), new XElement(w + "br"), new XElement(w + "t", "3 تیسرا۔"), new XElement(w + "br"), new XElement(w + "t", "2 دوسرا۔")), new XElement(w + "r", new XElement(w + "br"), new XElement(w + "t", "2")), new XElement(w + "r", new XElement(w + "t", "5 Text.")))))).Save(stream);
            var scan = type.GetMethod("Scan")!.Invoke(null, [input, CanonProfile.ProtestantNt])!;
            if ((int)scan.GetType().GetProperty("TotalVerseCount")!.GetValue(scan)! != 4)
                failures.Add("logical line scan: expected four explicit verses");
            var issues = (IEnumerable)scan.GetType().GetProperty("Issues")!.GetValue(scan)!;
            if (!issues.Cast<object>().Any(x => Equals(x.GetType().GetProperty("Code")!.GetValue(x), "VERSE_OUT_OF_ORDER")))
                failures.Add("logical line scan: reversed verse order was not reported");
            var output = Path.Combine(root, "output.docx");
            type.GetMethod("Standardize")!.Invoke(null, [input, output, CanonProfile.ProtestantNt, new HashSet<string> { "ROM" }, new HashSet<string>(), false]);
            using var result = ZipFile.OpenRead(output);
            using var resultStream = result.GetEntry("word/document.xml")!.Open();
            var paragraphs = XDocument.Load(resultStream).Descendants(w + "p").Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value))).ToArray();
            if (!paragraphs.SequenceEqual(new[] { "رومیوں", "باب ۱", "1 پس ایک۔", "3 تیسرا۔", "2 دوسرا۔", "25  Text." }))
                failures.Add("standardization: selected ROM text or original verse order changed");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"Detection tests exercised {checkedHeadings} profile headings and codes, sentence negatives, and RTL line/order preservation.");
    }
}
