using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using UsfmIntegrityStudio.Models;
using UsfmTools.Text;

var cases = new (string Name, string Input, string Expected)[]
{
    ("space before sentence punctuation", "لفظ !", "لفظ!"),
    ("parenthesis interior and leading spacing", "گھوٹ( دُولہے)", "گھوٹ (دُولہے)"),
    ("exclamation glued to next word", "ربی!ہرا", "ربی! ہرا"),
    ("question glued to next word", "سوال؟جواب", "سوال؟ جواب"),
    ("opening quote after Arabic comma", "آکھیا،’’حضرات", "آکھیا، ”حضرات"),
    ("directional quote interior spacing", "’’ سلام ‘‘کہا", "”سلام“ کہا"),
    ("single directional quote interior spacing", "’ امانت ‘", "’امانت‘"),
    ("question plus Urdu full stop", "کیتے؟۔", "کیتے؟"),
    ("question plus ASCII full stop", "کیتے؟.", "کیتے؟"),
    ("exclamation plus Urdu full stop", "کیتے!۔", "کیتے!"),
    ("exclamation plus ASCII full stop", "کیتے!.", "کیتے!"),
    ("verse marker with Urdu full stop", "\\v 12۔ سارے حیران", "\\v 12 سارے حیران"),
    ("verse marker with ASCII full stop", "\\v 12. سارے حیران", "\\v 12 سارے حیران"),
    ("verse marker with no space after punctuation", "\\v 6۔جیہڑے", "\\v 6 جیہڑے"),
    ("closing quote before full stop", "زیادہ بابرکت ہِے‘‘۔", "زیادہ بابرکت ہِے۔“"),
    ("closing quote after question should drop extra full stop", "مطلب ہے؟‘‘۔", "مطلب ہے؟“"),
    ("closing quote after exclamation should drop extra full stop", "ختم!‘‘۔", "ختم!“"),
    ("space before parenthesis after Urdu full stop", "اختیار ڈِتا گیا ہے۔(اوُں نے آکھے)", "اختیار ڈِتا گیا ہے۔ (اوُں نے آکھے)"),
    ("mixed quote pair opening", "آکھیا، ’‘ اوہ ڈینہ", "آکھیا، ”اوہ ڈینہ"),
    ("mixed quote pair closing", "پیچھوں ٹُر پوونا ’‘۔", "پیچھوں ٹُر پوونا۔“"),
    ("straight double quotes use Urdu direction", "کہا، \"سلام۔\"", "کہا، ”سلام۔“"),
    ("nested straight single quotes inside direct speech", "کہا، \"یہ 'امانت' ہے۔\"", "کہا، ”یہ ’امانت‘ ہے۔“"),
    ("john 8 quote direction", "\\v 34 یِسُوعؔ نے اُنھیں جواباً کہا، ’’مَیں تُم سے سچ سچ کہتا ہُوں۔", "\\v 34 یِسُوعؔ نے اُنھیں جواباً کہا، ”مَیں تُم سے سچ سچ کہتا ہُوں۔"),
    ("combined known cases", "اس نے ’’ سلام ‘‘کہا،’’حضرات!ہرا گھوٹ( دُولہے) سوال؟جواب کیتے؟۔ \\v 6۔جیہڑے ہِے‘‘۔", "اس نے ”سلام“ کہا، ”حضرات! ہرا گھوٹ (دُولہے) سوال؟ جواب کیتے؟ \\v 6 جیہڑے ہِے۔“"),
};

var failures = new List<string>();
var appAssembly = typeof(UsfmProjectCleanerService).Assembly;
var appMetadata = appAssembly
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .GroupBy(item => item.Key, StringComparer.Ordinal)
    .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.Ordinal);
AssertMetadata(appMetadata, "RepositoryUrl", "https://github.com/salmon84/usfm-integrity-studio-premium-redesign", failures);
AssertMetadataPresent(appMetadata, "SourceRevisionId", failures);
AssertMetadataPresent(appMetadata, "BuildChannel", failures);
AssertMetadataPresent(appMetadata, "OfficialBuild", failures);
AssertExpectedMetadataFromEnvironment(appMetadata, "SourceRevisionId", "UIS_EXPECT_REVISION", failures);
AssertExpectedMetadataFromEnvironment(appMetadata, "BuildChannel", "UIS_EXPECT_CHANNEL", failures);
AssertExpectedMetadataFromEnvironment(appMetadata, "OfficialBuild", "UIS_EXPECT_OFFICIAL", failures);

foreach (var test in cases)
{
    var actual = ScripturePunctuationNormalizer.NormalizeArabicDerivedSpacing(test.Input);
    var secondPass = ScripturePunctuationNormalizer.NormalizeArabicDerivedSpacing(actual);
    if (!string.Equals(actual, test.Expected, StringComparison.Ordinal))
    {
        failures.Add($"{test.Name}: expected [{test.Expected}] but got [{actual}]");
    }

    if (!string.Equals(secondPass, actual, StringComparison.Ordinal))
    {
        failures.Add($"{test.Name}: not idempotent; second pass produced [{secondPass}]");
    }
}

var structuralRoot = Path.Combine(Path.GetTempPath(), $"uis-docx-structure-{Guid.NewGuid():N}");
try
{
    Directory.CreateDirectory(structuralRoot);
    var structuralInput = Path.Combine(structuralRoot, "input.docx");
    var structuralOutput = Path.Combine(structuralRoot, "standardized.docx");
    var structuralSecondPass = Path.Combine(structuralRoot, "standardized-second.docx");
    WriteStructuralDocxFixture(structuralInput);
    var originalBytes = File.ReadAllBytes(structuralInput);

    InvokeDocxStandardize(appAssembly, structuralInput, structuralOutput);
    InvokeDocxStandardize(appAssembly, structuralOutput, structuralSecondPass);

    var expectedParagraphs = new[]
    {
        "مَتّی کی اِنجِیل",
        "باب ۱",
        "1 Verse one.",
        "2 Verse two.",
        "باب 12",
        "1 Verse twelve one.",
        "2 Verse twelve two.",
        "باب ۱۳",
        "1 Verse thirteen one.",
        "2 Verse thirteen two.",
        "باب ۱۴",
        "1 Verse fourteen begins",
        "continued verse fourteen text",
        "2 Verse fourteen two.",
        "باب ۱۵",
        "17 Verse fifteen seventeen.",
        "18 Verse fifteen eighteen.",
        "19 Verse fifteen nineteen.",
        "باب ۲۳",
        "37 Verse twenty-three thirty-seven.",
        "38 Verse twenty-three thirty-eight.",
        "39 Verse twenty-three thirty-nine.",
        "باب ۲۸",
        "1 First verse text.",
        "2 Second verse text."
    };
    var firstPassParagraphs = ReadDocxParagraphs(structuralOutput);
    var secondPassParagraphs = ReadDocxParagraphs(structuralSecondPass);
    if (!firstPassParagraphs.SequenceEqual(expectedParagraphs, StringComparer.Ordinal))
    {
        failures.Add(
            "structural DOCX standardization: unexpected paragraphs ["
            + string.Join(" | ", firstPassParagraphs)
            + "]");
    }

    if (!secondPassParagraphs.SequenceEqual(firstPassParagraphs, StringComparer.Ordinal))
    {
        failures.Add("structural DOCX standardization: second pass changed paragraph structure or text");
    }

    using (var standardizedArchive = ZipFile.OpenRead(structuralOutput))
    {
        var documentXml = ReadEntry(standardizedArchive, "word/document.xml");
        if (documentXml.Contains("vertAlign", StringComparison.Ordinal)
            || documentXml.Contains("۱۔", StringComparison.Ordinal)
            || documentXml.Contains("۲۔", StringComparison.Ordinal))
        {
            failures.Add("structural DOCX standardization: duplicate/superscript verse markers remain");
        }
    }

    if (!File.ReadAllBytes(structuralInput).SequenceEqual(originalBytes))
    {
        failures.Add("structural DOCX standardization: modified the source DOCX");
    }

    var conversionOutput = Path.Combine(structuralRoot, "bundled-conversion");
    Directory.CreateDirectory(conversionOutput);
    var staleUsfmPath = Path.Combine(conversionOutput, "stale.usfm");
    File.WriteAllText(staleUsfmPath, "\\id GEN\n\\c 1\n\\v 1 stale\n", new UTF8Encoding(false));
    var conversion = DocxConversionService.Execute(new DocxConversionRequest(
        structuralOutput,
        conversionOutput,
        Path.Combine(conversionOutput, "conversion-report.txt"),
        "permissive",
        "protestant-nt",
        "urd",
        ["MAT"],
        PreserveVerseMarkers: true));
    if (!conversion.GeneratedOutput || conversion.ExitCode is not (0 or 2))
    {
        failures.Add(
            $"bundled DOCX conversion: expected generated output with exit 0 or 2, got exit {conversion.ExitCode}; stderr [{conversion.StandardError}]");
    }
    if (conversion.GeneratedUsfmPaths.Contains(staleUsfmPath, StringComparer.OrdinalIgnoreCase))
    {
        failures.Add("bundled DOCX conversion: reported an unchanged stale USFM file as current-run output");
    }

    var identicalRetry = DocxConversionService.Execute(new DocxConversionRequest(
        structuralOutput,
        conversionOutput,
        Path.Combine(conversionOutput, "conversion-retry-report.txt"),
        "permissive",
        "protestant-nt",
        "urd",
        ["MAT"],
        PreserveVerseMarkers: true));
    if (!identicalRetry.GeneratedOutput || identicalRetry.GeneratedUsfmPaths.Count != 1)
    {
        failures.Add("bundled DOCX conversion: an identical retry was incorrectly reported as producing no output");
    }

    var convertedUsfmPath = conversion.GeneratedUsfmPaths.SingleOrDefault();
    if (convertedUsfmPath is null)
    {
        failures.Add($"bundled DOCX conversion: expected exactly one MAT output, got {conversion.GeneratedUsfmPaths.Count}");
    }
    else
    {
        var convertedUsfm = File.ReadAllText(convertedUsfmPath);
        if (!convertedUsfm.Contains("\\id MAT", StringComparison.OrdinalIgnoreCase)
            || !convertedUsfm.Contains("\\c 1", StringComparison.Ordinal)
            || !convertedUsfm.Contains("\\v 1 Verse one.", StringComparison.Ordinal)
            || !convertedUsfm.Contains("\\c 28", StringComparison.Ordinal)
            || !convertedUsfm.Contains("\\v 2 Second verse text.", StringComparison.Ordinal))
        {
            failures.Add($"bundled DOCX conversion: MAT identity, chapter order, or verse anchoring was not preserved; output [{convertedUsfm.Replace(Environment.NewLine, " | ")}]");
        }

        var convertedPackage = BttwProjectPackageService.PackageUsfm(convertedUsfmPath, "urd");
        if (!File.Exists(convertedPackage.TstudioPath)
            || !string.Equals(convertedPackage.ProjectId, "MAT", StringComparison.Ordinal)
            || convertedPackage.ChapterCount != 7)
        {
            failures.Add("bundled DOCX conversion: BTTW project packaging did not preserve the converted MAT structure");
        }
    }
}
finally
{
    if (Directory.Exists(structuralRoot))
    {
        Directory.Delete(structuralRoot, recursive: true);
    }
}

var tempRoot = Path.Combine(Path.GetTempPath(), $"uis-punctuation-regression-{Guid.NewGuid():N}");
try
{
    var sourceRoot = Path.Combine(tempRoot, "source");
    var projectRoot = Path.Combine(sourceRoot, "ur_jhn_text_ulb");
    var chapterRoot = Path.Combine(projectRoot, "08");
    Directory.CreateDirectory(chapterRoot);

    File.WriteAllText(
        Path.Combine(projectRoot, "manifest.json"),
        """
        {
          "project": { "id": "JHN", "name": "John" },
          "resource": { "id": "ulb", "name": "ULB" },
          "format": "usfm",
          "finished_chunks": []
        }
        """,
        new UTF8Encoding(false));

    const string chunkInput = "\\v 28 کہا، “ جب تُم آئے۔ \\v 29 جواب دیا۔ ”";
    const string chunkExpected = "\\v 28 کہا، ”جب تُم آئے۔ \\v 29 جواب دیا۔“";
    File.WriteAllText(Path.Combine(chapterRoot, "28.txt"), chunkInput, new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(chapterRoot, "30.txt"), "\\v 30 اس نے کہ ’سلام‘۔", new UTF8Encoding(false));

    var explicitSourceRoot = Path.Combine(tempRoot, "configured-source");
    Directory.CreateDirectory(explicitSourceRoot);
    File.WriteAllText(
        Path.Combine(explicitSourceRoot, "44-JHN.usfm"),
        "\\id JHN\n\\c 8\n\\v 30 He said, \"Hello.\"\n",
        new UTF8Encoding(false));

    var inputProject = Path.Combine(tempRoot, "input.tstudio");
    var outputProject = Path.Combine(tempRoot, "output.tstudio");
    ZipFile.CreateFromDirectory(sourceRoot, inputProject, CompressionLevel.Fastest, includeBaseDirectory: false);
    var sourceAwareResult = UsfmProjectCleanerService.Clean(
        inputProject,
        outputProject,
        CanonProfile.ProtestantNt,
        explicitSourceRoot);
    if (sourceAwareResult.DirectSpeechFixes != 1)
    {
        failures.Add($"configured source-USFM folder: expected one source-aware direct-speech fix, got {sourceAwareResult.DirectSpeechFixes}");
    }

    using var cleanedArchive = ZipFile.OpenRead(outputProject);
    var chunkEntry = cleanedArchive.GetEntry("ur_jhn_text_ulb/08/28.txt");
    if (chunkEntry is null)
    {
        failures.Add("tstudio RTL quote spacing: cleaned chunk is missing");
    }
    else
    {
        using var reader = new StreamReader(chunkEntry.Open(), Encoding.UTF8);
        var actual = reader.ReadToEnd();
        if (!string.Equals(actual, chunkExpected, StringComparison.Ordinal))
        {
            failures.Add($"tstudio RTL quote spacing: expected [{chunkExpected}] but got [{actual}]");
        }
    }


    var sourceAwareEntry = cleanedArchive.GetEntry("ur_jhn_text_ulb/08/30.txt");
    if (sourceAwareEntry is null)
    {
        failures.Add("configured source-USFM folder: source-aware chunk is missing");
    }
    else
    {
        using var reader = new StreamReader(sourceAwareEntry.Open(), Encoding.UTF8);
        var actual = reader.ReadToEnd();
        if (actual.Contains("کہ", StringComparison.Ordinal))
        {
            failures.Add($"configured source-USFM folder: redundant direct-speech کہ was not removed [{actual}]");
        }
    }
}
finally
{
    if (Directory.Exists(tempRoot))
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}

var integrityRoot = Path.Combine(Path.GetTempPath(), $"uis-project-integrity-{Guid.NewGuid():N}");
try
{
    Directory.CreateDirectory(integrityRoot);
    var usfmPath = Path.Combine(integrityRoot, "2co.usfm");
    File.WriteAllText(
        usfmPath,
        """
        \id 2CO Regular
        \ide UTF-8
        \h 2 Corinthians
        \toc1 2 Corinthians
        \toc2 2 Corinthians
        \toc3 2CO
        \mt 2 Corinthians
        \c 1
        \v 17 Verse seventeen.
        \v 18 Verse eighteen.
        \v 19 Verse nineteen.
        \v 20 Verse twenty.
        """,
        new UTF8Encoding(false));

    var package = BttwProjectPackageService.PackageUsfm(usfmPath, "und");
    using (var archive = ZipFile.OpenRead(package.TstudioPath))
    {
        AssertEntryExists(archive, "und_2co_text_reg/01/17.txt", failures);
        AssertEntryExists(archive, "und_2co_text_reg/01/19.txt", failures);
        if (archive.GetEntry("und_2co_text_reg/01/18.txt") is not null)
        {
            failures.Add("canonical BTTW packaging: noncanonical 01/18.txt was generated");
        }

        var chunk17 = ReadEntry(archive, "und_2co_text_reg/01/17.txt");
        if (!chunk17.Contains(@"\v 17 Verse seventeen.", StringComparison.Ordinal)
            || !chunk17.Contains(@"\v 18 Verse eighteen.", StringComparison.Ordinal)
            || chunk17.Contains(@"\v 19", StringComparison.Ordinal))
        {
            failures.Add($"canonical BTTW packaging: 01/17.txt has incorrect verse coverage [{chunk17}]");
        }

        var outerManifest = ReadEntry(archive, "manifest.json");
        if (!outerManifest.Contains(@"""build"": ""1073x""", StringComparison.Ordinal))
        {
            failures.Add("canonical BTTW packaging: outer manifest generator build is not 1073x");
        }
    }

    var partialUsfmPath = Path.Combine(integrityRoot, "2co-partial.usfm");
    File.WriteAllText(
        partialUsfmPath,
        """
        \id 2CO Regular
        \h 2 Corinthians
        \c 1
        \v 18 Partial verse eighteen.
        """,
        new UTF8Encoding(false));
    var partialPackage = BttwProjectPackageService.PackageUsfm(partialUsfmPath, "und");
    using (var archive = ZipFile.OpenRead(partialPackage.TstudioPath))
    {
        AssertEntryExists(archive, "und_2co_text_reg/01/17.txt", failures);
        if (archive.GetEntry("und_2co_text_reg/01/18.txt") is not null)
        {
            failures.Add("partial canonical BTTW packaging: verse 18 was incorrectly emitted as 01/18.txt");
        }
    }

    var lukeUsfmPath = Path.Combine(integrityRoot, "luk17.usfm");
    File.WriteAllText(
        lukeUsfmPath,
        """
        \id LUK Regular
        \h Luke
        \c 17
        \v 34 Verse thirty-four.
        \v 35 Verse thirty-five.
        \v 36 Verse thirty-six.
        \v 37 Verse thirty-seven.
        """,
        new UTF8Encoding(false));
    var lukePackage = BttwProjectPackageService.PackageUsfm(lukeUsfmPath, "und");
    using (var archive = ZipFile.OpenRead(lukePackage.TstudioPath))
    {
        AssertEntryExists(archive, "und_luk_text_reg/17/34.txt", failures);
        if (archive.GetEntry("und_luk_text_reg/17/37.txt") is not null)
        {
            failures.Add("Luke canonical BTTW packaging: noncanonical 17/37.txt was generated");
        }

        var chunk34 = ReadEntry(archive, "und_luk_text_reg/17/34.txt");
        foreach (var verse in new[] { 34, 35, 36, 37 })
        {
            if (!chunk34.Contains($@"\v {verse} Verse", StringComparison.Ordinal))
            {
                failures.Add($"Luke canonical BTTW packaging: verse {verse} is missing from 17/34.txt [{chunk34}]");
            }
        }

        var innerManifest = ReadEntry(archive, "und_luk_text_reg/manifest.json");
        if (innerManifest.Contains(@"""17-37""", StringComparison.Ordinal))
        {
            failures.Add("Luke canonical BTTW packaging: noncanonical 17-37 finished chunk was generated");
        }
    }

    var contaminatedSource = Path.Combine(integrityRoot, "contaminated-source");
    var contaminatedProject = Path.Combine(contaminatedSource, "ur_2co_text_ulb");
    Directory.CreateDirectory(Path.Combine(contaminatedProject, "01"));
    WriteTestManifest(contaminatedProject, "2CO", "01-18");
    File.WriteAllText(Path.Combine(contaminatedProject, "01", "18.txt"), @"\v 18 Foreign chunk.", new UTF8Encoding(false));

    var contaminatedInput = Path.Combine(integrityRoot, "contaminated.tstudio");
    var contaminatedOutput = Path.Combine(integrityRoot, "contaminated-cleaned.tstudio");
    ZipFile.CreateFromDirectory(contaminatedSource, contaminatedInput, CompressionLevel.Fastest, includeBaseDirectory: false);
    var originalBytes = File.ReadAllBytes(contaminatedInput);
    var warningResult = UsfmProjectCleanerService.Clean(contaminatedInput, contaminatedOutput, CanonProfile.ProtestantNt);
    if (!File.Exists(contaminatedOutput))
    {
        failures.Add("chunk-layout warning: noncanonical 2CO 01/18.txt incorrectly blocked cleaned output");
    }

    if (!warningResult.VerificationIssues.Any(issue =>
            issue.Contains("WARNING NONCANONICAL_CHUNK_PATH: 2CO 01/18.txt", StringComparison.Ordinal))
        || !warningResult.VerificationIssues.Any(issue =>
            issue.Contains("WARNING NONCANONICAL_FINISHED_CHUNK: 2CO 01-18", StringComparison.Ordinal)))
    {
        failures.Add("chunk-layout warning: cleaned result did not report both noncanonical path alerts");
    }

    using (var warningArchive = ZipFile.OpenRead(contaminatedOutput))
    {
        var preservedChunk = ReadEntry(warningArchive, "ur_2co_text_ulb/01/18.txt");
        if (!preservedChunk.Contains(@"\v 18 Foreign chunk.", StringComparison.Ordinal))
        {
            failures.Add("chunk-layout warning: noncanonical chunk scripture was not preserved");
        }
    }

    if (!File.ReadAllBytes(contaminatedInput).SequenceEqual(originalBytes))
    {
        failures.Add("chunk-layout warning: modified the input project archive");
    }

    var lukeSource = Path.Combine(integrityRoot, "luke-split-source");
    var lukeProject = Path.Combine(lukeSource, "pnb_luk_text_reg");
    Directory.CreateDirectory(Path.Combine(lukeProject, "17"));
    WriteTestManifest(lukeProject, "LUK", "17-37");
    File.WriteAllText(
        Path.Combine(lukeProject, "17", "34.txt"),
        @"\v 34 Verse thirty-four. \v 35 Verse thirty-five. \v 36 Verse thirty-six.",
        new UTF8Encoding(false));
    File.WriteAllText(
        Path.Combine(lukeProject, "17", "37.txt"),
        @"\v 37 Verse thirty-seven.",
        new UTF8Encoding(false));

    var lukeInput = Path.Combine(integrityRoot, "luke-split.tstudio");
    var lukeOutput = Path.Combine(integrityRoot, "luke-split-cleaned.tstudio");
    ZipFile.CreateFromDirectory(lukeSource, lukeInput, CompressionLevel.Fastest, includeBaseDirectory: false);
    var lukeResult = UsfmProjectCleanerService.Clean(lukeInput, lukeOutput, CanonProfile.ProtestantNt);
    if (!lukeResult.VerificationIssues.Any(issue =>
            issue.Contains("WARNING NONCANONICAL_CHUNK_PATH: LUK 17/37.txt", StringComparison.Ordinal))
        || !lukeResult.VerificationIssues.Any(issue =>
            issue.Contains("WARNING NONCANONICAL_FINISHED_CHUNK: LUK 17-37", StringComparison.Ordinal)))
    {
        failures.Add("Luke split warning: 17/37 was not reported without blocking");
    }

    using (var lukeArchive = ZipFile.OpenRead(lukeOutput))
    {
        var verse37 = ReadEntry(lukeArchive, "pnb_luk_text_reg/17/37.txt");
        if (!verse37.Contains(@"\v 37 Verse thirty-seven.", StringComparison.Ordinal))
        {
            failures.Add("Luke split warning: 17/37 scripture was changed or removed");
        }
    }

    var duplicateSource = Path.Combine(integrityRoot, "duplicate-source");
    var duplicateProject = Path.Combine(duplicateSource, "ur_2co_text_ulb");
    Directory.CreateDirectory(Path.Combine(duplicateProject, "01"));
    WriteTestManifest(duplicateProject, "2CO", "01-18");
    File.WriteAllText(Path.Combine(duplicateProject, "01", "17.txt"), @"\v 17 Verse seventeen. \v 18 Original verse eighteen.", new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(duplicateProject, "01", "18.txt"), @"\v 18 Duplicate verse eighteen.", new UTF8Encoding(false));

    var duplicateInput = Path.Combine(integrityRoot, "duplicate.tstudio");
    var duplicateOutput = Path.Combine(integrityRoot, "duplicate-cleaned.tstudio");
    ZipFile.CreateFromDirectory(duplicateSource, duplicateInput, CompressionLevel.Fastest, includeBaseDirectory: false);
    try
    {
        UsfmProjectCleanerService.Clean(duplicateInput, duplicateOutput, CanonProfile.ProtestantNt);
        failures.Add("contamination gate: duplicate verse coverage was not blocked");
    }
    catch (InvalidDataException ex)
    {
        if (!ex.Message.Contains("DUPLICATE_VERSE_COVERAGE", StringComparison.Ordinal))
        {
            failures.Add($"contamination gate: duplicate error was not identified [{ex.Message}]");
        }
    }

    if (File.Exists(duplicateOutput))
    {
        failures.Add("contamination gate: created output despite duplicate verse coverage");
    }

    var cleanSource = Path.Combine(integrityRoot, "clean-source");
    var cleanProject = Path.Combine(cleanSource, "ur_2co_text_ulb");
    Directory.CreateDirectory(Path.Combine(cleanProject, "01"));
    WriteTestManifest(cleanProject, "2CO", "01-17");
    File.WriteAllText(Path.Combine(cleanProject, "01", "17.txt"), @"\v 17 Verse seventeen. \v 18 Verse eighteen.", new UTF8Encoding(false));

    var cleanInput = Path.Combine(integrityRoot, "clean.tstudio");
    var cleanOutput = Path.Combine(integrityRoot, "clean-output.tstudio");
    ZipFile.CreateFromDirectory(cleanSource, cleanInput, CompressionLevel.Fastest, includeBaseDirectory: false);
    UsfmProjectCleanerService.Clean(cleanInput, cleanOutput, CanonProfile.ProtestantNt);
    if (!File.Exists(cleanOutput))
    {
        failures.Add("contamination gate: rejected a canonical 2CO project");
    }
}
finally
{
    if (Directory.Exists(integrityRoot))
    {
        Directory.Delete(integrityRoot, recursive: true);
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Punctuation regression test failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine("- " + failure);
    }
    Environment.Exit(1);
}

Console.WriteLine(
    $"Regression tests passed: build identity metadata, {cases.Length} punctuation cases, structural DOCX standardization, bundled DOCX conversion, explicit source-USFM lookup, quote-cleaning .tstudio, canonical BTTW packaging, partial-chunk mapping, warning-only extra chunk splits, and non-destructive duplicate blocking.");

static void InvokeDocxStandardize(Assembly appAssembly, string inputPath, string outputPath)
{
    var serviceType = appAssembly.GetType("UsfmIntegrityStudio.Models.DocxScanService")
        ?? throw new InvalidOperationException("DocxScanService type was not found.");
    var method = serviceType.GetMethod("Standardize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("DocxScanService.Standardize was not found.");
    method.Invoke(
        null,
        [
            inputPath,
            outputPath,
            CanonProfile.ProtestantNt,
            new HashSet<string>(["MAT"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            true
        ]);
}

static void WriteStructuralDocxFixture(string path)
{
    XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    var document = new XDocument(
        new XElement(
            w + "document",
            new XAttribute(XNamespace.Xmlns + "w", w),
            new XElement(
                w + "body",
                Paragraph(w, "مَتّی کی اِنجِیل"),
                Paragraph(w, "باب ۱"),
                new XElement(
                    w + "p",
                    SuperscriptRun(w, "1"),
                    Run(w, "\u00A0", preserveSpace: true),
                    Run(w, "۱"),
                    Run(w, "۔\u00A0", preserveSpace: true),
                    Run(w, "Verse one."),
                    SuperscriptRun(w, "2"),
                    Run(w, " ۲۔ Verse two.", preserveSpace: true)),
                new XElement(
                    w + "p",
                    new XElement(
                        w + "r",
                        Text(w, "باب12"),
                        new XElement(w + "br"),
                        Text(w, "1۔ Verse twelve one."),
                        new XElement(w + "br"),
                        Text(w, "2 Verse twelve two."))),
                Paragraph(w, "باب ۱۳"),
                new XElement(
                    w + "p",
                    SuperscriptRun(w, "1"),
                    Run(w, "Verse thirteen one.")),
                new XElement(
                    w + "p",
                    SuperscriptRun(w, "2"),
                    Run(w, "Verse thirteen two.")),
                Paragraph(w, "باب ۱۴"),
                Paragraph(w, "1 Verse fourteen begins"),
                Paragraph(w, "continued verse fourteen text"),
                Paragraph(w, "2 Verse fourteen two."),
                Paragraph(w, "باب ۱۵"),
                new XElement(
                    w + "p",
                    SuperscriptRun(w, "17"),
                    Run(w, " ۱۷۔ Verse fifteen seventeen.", preserveSpace: true),
                    SuperscriptRun(w, "18"),
                    Run(w, "۸۔ Verse fifteen eighteen."),
                    SuperscriptRun(w, "19"),
                    Run(w, " ۱۹۔ Verse fifteen nineteen.", preserveSpace: true)),
                Paragraph(w, "باب ۲۳"),
                new XElement(
                    w + "p",
                    SuperscriptRun(w, "37"),
                    Run(w, " ۳۷۔ Verse twenty-three thirty-seven.", preserveSpace: true),
                    SuperscriptRun(w, "8"),
                    Run(w, "۳۸۔ Verse twenty-three thirty-eight."),
                    SuperscriptRun(w, "39"),
                    Run(w, " ۳۹۔ Verse twenty-three thirty-nine.", preserveSpace: true)),
                Paragraph(w, "باب ۲۸"),
                Paragraph(w, string.Empty),
                new XElement(
                    w + "p",
                    new XElement(
                        w + "r",
                        Text(w, "First verse text."),
                        new XElement(w + "br"),
                        Text(w, "2 Second verse text."))),
                new XElement(w + "sectPr"))));

    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    var entry = archive.CreateEntry("word/document.xml");
    using var stream = entry.Open();
    document.Save(stream);
}

static XElement Paragraph(XNamespace w, string value)
{
    return new XElement(w + "p", Run(w, value));
}

static XElement SuperscriptRun(XNamespace w, string value)
{
    return new XElement(
        w + "r",
        new XElement(w + "rPr", new XElement(w + "vertAlign", new XAttribute(w + "val", "superscript"))),
        Text(w, value));
}

static XElement Run(XNamespace w, string value, bool preserveSpace = false)
{
    return new XElement(w + "r", Text(w, value, preserveSpace));
}

static XElement Text(XNamespace w, string value, bool preserveSpace = false)
{
    var text = new XElement(w + "t", value);
    if (preserveSpace)
    {
        text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
    }

    return text;
}

static IReadOnlyList<string> ReadDocxParagraphs(string path)
{
    XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    using var archive = ZipFile.OpenRead(path);
    var entry = archive.GetEntry("word/document.xml")
        ?? throw new InvalidDataException("Synthetic DOCX is missing word/document.xml.");
    using var stream = entry.Open();
    var document = XDocument.Load(stream);
    return document
        .Descendants(w + "p")
        .Select(paragraph => string.Concat(paragraph.Descendants(w + "t").Select(text => text.Value)).Trim())
        .Where(text => !string.IsNullOrWhiteSpace(text))
        .ToList();
}

static void AssertMetadata(
    IReadOnlyDictionary<string, string> metadata,
    string key,
    string expected,
    ICollection<string> failures)
{
    if (!metadata.TryGetValue(key, out var actual) || !string.Equals(actual, expected, StringComparison.Ordinal))
    {
        failures.Add($"build identity: expected {key} [{expected}] but got [{actual ?? "missing"}]");
    }
}

static void AssertMetadataPresent(
    IReadOnlyDictionary<string, string> metadata,
    string key,
    ICollection<string> failures)
{
    if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
    {
        failures.Add($"build identity: missing {key}");
    }
}

static void AssertExpectedMetadataFromEnvironment(
    IReadOnlyDictionary<string, string> metadata,
    string metadataKey,
    string environmentKey,
    ICollection<string> failures)
{
    var expected = Environment.GetEnvironmentVariable(environmentKey);
    if (!string.IsNullOrWhiteSpace(expected))
    {
        AssertMetadata(metadata, metadataKey, expected, failures);
    }
}

static void AssertEntryExists(ZipArchive archive, string entryName, ICollection<string> failures)
{
    if (archive.GetEntry(entryName) is null)
    {
        failures.Add($"canonical BTTW packaging: missing {entryName}");
    }
}

static string ReadEntry(ZipArchive archive, string entryName)
{
    var entry = archive.GetEntry(entryName)
        ?? throw new InvalidDataException($"Missing test archive entry: {entryName}");
    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
    return reader.ReadToEnd();
}

static void WriteTestManifest(string projectRoot, string bookId, string finishedChunk)
{
    File.WriteAllText(
        Path.Combine(projectRoot, "manifest.json"),
        $$"""
        {
          "package_version": 8,
          "format": "usfm",
          "generator": { "name": "ts-desktop", "build": "1073x" },
          "project": { "id": "{{bookId}}", "name": "{{bookId}}" },
          "resource": { "id": "ulb", "name": "ULB" },
          "finished_chunks": [ "{{finishedChunk}}" ]
        }
        """,
        new UTF8Encoding(false));
}
