using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UsfmConverter.Contracts;
using UsfmTools.Text;

namespace UsfmIntegrityStudio.ConverterRuntime;

internal static class DocxToUsfmConverter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Cp = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
    private static readonly XNamespace Vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
    private static readonly Regex ChapterRegex = new(
        "^(?:Глава|Chapter|باب)\\s*([0-9\\u0660-\\u0669\\u06F0-\\u06F9]+)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex BookTitleRegex = new(
        "^(?:(?:\\d+|ПЕРВАЯ|ВТОРАЯ|ТРЕТЬЯ|ЧЕТВЕРТАЯ|I|II|III|IV)\\s+)?(?:Книга|КНИГА|Book)\\s+.+\\((?:\\d+\\s*)?[A-Za-z][^)]+\\)\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex StandaloneBookTitleRegex = new(
        "^(?:\\d+\\s+[^()]{1,120}|ПСАЛТЫРЬ|Псалтирь)\\s*\\((?:\\d+\\s*)?[A-Za-z][^)]+\\)\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlainBookTitleRegex = new(
        "^(?:(?:\\d+|ПЕРВАЯ|ВТОРАЯ|ТРЕТЬЯ|ЧЕТВЕРТАЯ|ЧЕТВЁРТАЯ|I|II|III|IV)\\s+)?(?:Книга(?:\\s+пророка)?|КНИГА(?:\\s+ПРОРОКА)?|Book)\\s+[\\p{L}\\p{M}\\s\\-]{2,140}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex EquivalentBookTitleRegex = new(
        "^(?:\\d+\\s+)?(?:Книга\\s+)?[\\p{L}\\p{M}\\s\\-]{2,80}\\s*=\\s*(?:\\d+\\s*)?[A-Za-z][A-Za-z\\s\\-]{1,80}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex NumericBookTitleRegex = new(
        "^(?:\\d+|I|II|III|IV)\\s+(?:Книга\\s+|Book\\s+)?[\\p{L}\\p{M}][\\p{L}\\p{M}\\s\\-]{1,80}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex InlineBookMarkerRegex = new(
        "^(?<before>.*?[.!?])\\s+(?<title>(?:(?:\\d+|ПЕРВАЯ|ВТОРАЯ|ТРЕТЬЯ|ЧЕТВЕРТАЯ|I|II|III|IV)\\s+)?(?:(?:Книга|КНИГА|Book)\\s+[^()]{1,140}|(?:\\d+\\s+[^()]{1,120}|ПСАЛТЫРЬ|Псалтирь))\\((?:\\d+\\s*)?[A-Za-z][^)]+\\))\\s*(?<after>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VerseRegex = new(
        "^([0-9\\u0660-\\u0669\\u06F0-\\u06F9]{1,3})(?:\\s*[\\.)۔:]\\s*|\\s+)(.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VerseRegexLoose = new(
        "^([0-9\\u0660-\\u0669\\u06F0-\\u06F9]{1,3})(.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GenericTranslationLabelRegex = new(
        "^(?:سرائیکی\\s*ترجمہ|اُردو\\s*ترجمہ|اردو\\s*ترجمہ|translation)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex TranslatorCreditRegex = new(
        @"^[\p{L}\p{M}\s]{2,80}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UsfmVerseMarkerOnlyRegex = new(
        @"^\\v\s*([0-9\u0660-\u0669\u06F0-\u06F9]{1,3})\s*[\\.)۔:]?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex PlainVerseMarkerOnlyRegex = new(
        @"^([0-9\u0660-\u0669\u06F0-\u06F9]{1,3})\s*[.)۔:]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex UsfmVerseInlineRegex = new(
        @"^\\v\s*([0-9\u0660-\u0669\u06F0-\u06F9]{1,3})(?![0-9\u0660-\u0669\u06F0-\u06F9])\s*[\\.)۔:]?\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex VerseMarkerInsideDescriptionRegex = new(
        @"\\v\s*[0-9\u0660-\u0669\u06F0-\u06F9]{1,3}(?![0-9\u0660-\u0669\u06F0-\u06F9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex EmbeddedUsfmVerseMarkerRegex = new(
        @"\\v\s*([0-9\u0660-\u0669\u06F0-\u06F9]{1,3})(?![0-9\u0660-\u0669\u06F0-\u06F9])\s*[\\.)۔:]?\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex VSlashVerseMarkerOnlyRegex = new(
        @"^(?:(?:[-–—]+\s*)*)(?:[vV]\s*/?|/\s*[vV])\s*([0-9\u0660-\u0669\u06F0-\u06F9]{1,3})\s*[-–—]?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VSlashVerseInlineRegex = new(
        @"^(?:(?:[-–—]+\s*)*)(?:[vV]\s*/?|/\s*[vV])\s*([0-9\u0660-\u0669\u06F0-\u06F9]{1,3})\s*[-–—]?\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BookTitleWithInlineChapterRegex = new(
        @"^(?<title>.+?)\s+(?<keyword>Глава|Chapter|باب)\s*(?<chapter>[0-9\u0660-\u0669\u06F0-\u06F9]+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static ConversionResult Convert(
        string inputDocxPath,
        ContractMode mode,
        string canonToken = "protestant-ot",
        IReadOnlySet<string>? selectedBookIds = null,
        bool preserveVerseMarkers = false)
    {
        var issues = new List<ContractIssue>();
        var lines = new List<string>();

        if (!File.Exists(inputDocxPath))
        {
            issues.Add(Issue(mode, "DOCX_NOT_FOUND", $"Input DOCX file was not found: {inputDocxPath}"));
            return new ConversionResult(lines, issues);
        }

        try
        {
            using var archive = ZipFile.OpenRead(inputDocxPath);

            var documentXml = LoadRequiredXml(archive, "word/document.xml", mode, issues);
            if (documentXml is null)
            {
                return new ConversionResult(lines, issues);
            }

            var stylesXml = LoadOptionalXml(archive, "word/styles.xml", mode, issues);
            var footnotesXml = LoadOptionalXml(archive, "word/footnotes.xml", mode, issues);
            var customXml = LoadOptionalXml(archive, "docProps/custom.xml", mode, issues);

            var paragraphStyles = new HashSet<string>(StringComparer.Ordinal);
            var characterStyles = new HashSet<string>(StringComparer.Ordinal);
            CollectStyles(stylesXml, paragraphStyles, characterStyles);

            issues.AddRange(UsfmDocxContractV1.ValidateStyles(paragraphStyles, characterStyles, mode));

            var footnotesById = ParseFootnotes(footnotesXml);
            AppendHeaderFromCustomProperties(customXml, lines);

            var bodyLines = new List<string>();
            var contractResult = ConvertBodyUsingContract(documentXml, footnotesById, mode, bodyLines, issues);

            foreach (var unknownStyle in contractResult.UnknownParagraphStyleCounts.OrderBy(kvp => kvp.Key))
            {
                issues.Add(Issue(
                    mode,
                    "STYLE_UNKNOWN_PARAGRAPH",
                    $"Unknown paragraph style ID '{unknownStyle.Key}' encountered {unknownStyle.Value} time(s)."));
            }

            if (contractResult.VerseCount == 0)
            {
                var heuristicLines = ConvertBodyUsingHeuristics(documentXml, footnotesById, mode, issues, preserveVerseMarkers, selectedBookIds);
                if (heuristicLines.Count > 0)
                {
                    issues.Add(Issue(
                        mode,
                        "FALLBACK_HEURISTIC_USED",
                        "No contract verse styles detected; used heuristic paragraph parsing."));

                    bodyLines = heuristicLines;
                }
            }

            lines.AddRange(bodyLines);
            var embeddedChapterRepairCount = RepairEmbeddedChapterHeadings(lines);
            if (embeddedChapterRepairCount > 0)
            {
                issues.Add(Issue(
                    mode,
                    "EMBEDDED_CHAPTER_HEADING_RECOVERED",
                    $"Recovered {embeddedChapterRepairCount} embedded chapter heading(s) that were attached to verse text."));
            }

            ApplyKnownRussianVersificationFixes(lines, mode, issues, canonToken, selectedBookIds);
            if (!RejectVerseMarkersInsideDescriptions(lines, issues))
            {
                ValidateChapterVerseContinuity(lines, mode, issues);
            }

        }
        catch (InvalidDataException ex)
        {
            issues.Add(Issue(mode, "DOCX_INVALID", $"Invalid DOCX structure: {ex.Message}"));
        }
        catch (Exception ex)
        {
            issues.Add(Issue(mode, "DOCX_CONVERSION_FAILED", $"Unexpected conversion failure: {ex.Message}"));
        }

        return new ConversionResult(lines, issues);
    }

    private static bool RejectVerseMarkersInsideDescriptions(
        List<string> lines,
        ICollection<ContractIssue> issues)
    {
        var malformedLines = lines
            .Where(line => line.StartsWith("\\d ", StringComparison.Ordinal)
                           && VerseMarkerInsideDescriptionRegex.IsMatch(line))
            .ToList();
        if (malformedLines.Count == 0)
        {
            return false;
        }

        issues.Add(new ContractIssue(
            Severity.Error,
            "USFM_VERSE_MARKER_IN_DESCRIPTION",
            $"Refused conversion because {malformedLines.Count} description line(s) contain numbered verse markers."));
        lines.Clear();
        return true;
    }

    private static int RepairEmbeddedChapterHeadings(List<string> lines)
    {
        var repairCount = 0;
        for (var i = 0; i < lines.Count - 1; i++)
        {
            if (!lines[i].StartsWith("\\v ", StringComparison.Ordinal)
                || !lines[i + 1].StartsWith("\\v 1 ", StringComparison.Ordinal))
            {
                continue;
            }

            var match = Regex.Match(
                lines[i],
                @"^(?<body>\\v\s+\d+\s+.+?)\s+(?<chapterLine>(?:[\p{L}\p{M}]+\s+){0,4}(?:Глава|Chapter|باب)\s*(?<chapter>[0-9\u0660-\u0669\u06F0-\u06F9]+))\s*$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            if (!match.Success || !TryParseVerseOrChapterNumber(match.Groups["chapter"].Value, out var chapterNumber))
            {
                continue;
            }

            lines[i] = match.Groups["body"].Value.TrimEnd();
            lines.Insert(i + 1, $"\\c {chapterNumber}");
            lines.Insert(i + 2, $"\\cl {NormalizeChapterDisplayLine(match.Groups["chapterLine"].Value.Trim())}");
            repairCount++;
            i += 2;
        }

        return repairCount;
    }

    private static XDocument? LoadRequiredXml(ZipArchive archive, string entryName, ContractMode mode, ICollection<ContractIssue> issues)
    {
        var document = LoadOptionalXml(archive, entryName, mode, issues);
        if (document is null)
        {
            issues.Add(Issue(mode, "DOCX_ENTRY_MISSING", $"Required DOCX entry is missing: {entryName}"));
        }

        return document;
    }

    private static XDocument? LoadOptionalXml(ZipArchive archive, string entryName, ContractMode mode, ICollection<ContractIssue> issues)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            issues.Add(Issue(mode, "DOCX_XML_PARSE_FAILED", $"Failed to parse {entryName}: {ex.Message}"));
            return null;
        }
    }

    private static void CollectStyles(XDocument? stylesXml, ISet<string> paragraphStyles, ISet<string> characterStyles)
    {
        if (stylesXml?.Root is null)
        {
            return;
        }

        foreach (var style in stylesXml.Root.Elements(W + "style"))
        {
            var type = (string?)style.Attribute(W + "type") ?? string.Empty;
            var styleId = (string?)style.Attribute(W + "styleId");
            if (string.IsNullOrEmpty(styleId))
            {
                continue;
            }

            if (string.Equals(type, "paragraph", StringComparison.OrdinalIgnoreCase))
            {
                paragraphStyles.Add(styleId);
            }
            else if (string.Equals(type, "character", StringComparison.OrdinalIgnoreCase))
            {
                characterStyles.Add(styleId);
            }
        }
    }

    private static Dictionary<string, string> ParseFootnotes(XDocument? footnotesXml)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (footnotesXml?.Root is null)
        {
            return map;
        }

        foreach (var footnote in footnotesXml.Root.Elements(W + "footnote"))
        {
            var id = (string?)footnote.Attribute(W + "id");
            if (string.IsNullOrWhiteSpace(id) || id.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var text = ExtractNodeText(footnote).Trim();
            if (!string.IsNullOrEmpty(text))
            {
                map[id] = text;
            }
        }

        return map;
    }

    private static void AppendHeaderFromCustomProperties(XDocument? customXml, ICollection<string> lines)
    {
        if (customXml?.Root is null)
        {
            return;
        }

        var propertyByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in customXml.Root.Elements(Cp + "property"))
        {
            var name = (string?)prop.Attribute("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var value = prop.Element(Vt + "lpwstr")?.Value ?? prop.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                propertyByName[name] = value.Trim();
            }
        }

        AddHeaderLine(propertyByName, UsfmDocxContractV1.CustomProperties.Id, "\\id", lines);
        AddHeaderLine(propertyByName, UsfmDocxContractV1.CustomProperties.Ide, "\\ide", lines);
        AddHeaderLine(propertyByName, UsfmDocxContractV1.CustomProperties.H, "\\h", lines);
        AddHeaderLine(propertyByName, UsfmDocxContractV1.CustomProperties.Toc1, "\\toc1", lines);
        AddHeaderLine(propertyByName, UsfmDocxContractV1.CustomProperties.Toc2, "\\toc2", lines);
        AddHeaderLine(propertyByName, UsfmDocxContractV1.CustomProperties.Toc3, "\\toc3", lines);
    }

    private static void AddHeaderLine(
        IReadOnlyDictionary<string, string> propertyByName,
        string propertyName,
        string marker,
        ICollection<string> lines)
    {
        if (propertyByName.TryGetValue(propertyName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{marker} {value}");
        }
    }

    private static BodyConversionResult ConvertBodyUsingContract(
        XDocument documentXml,
        IReadOnlyDictionary<string, string> footnotesById,
        ContractMode mode,
        IList<string> lines,
        ICollection<ContractIssue> issues)
    {
        var verseCount = 0;
        var unknownParagraphStyleCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var body = documentXml.Root?
            .Element(W + "body");

        if (body is null)
        {
            issues.Add(Issue(mode, "DOCX_BODY_MISSING", "word/document.xml does not contain w:body."));
            return new BodyConversionResult(0, unknownParagraphStyleCounts);
        }

        foreach (var paragraph in body.Elements(W + "p"))
        {
            var paragraphStyle = paragraph
                .Element(W + "pPr")?
                .Element(W + "pStyle")?
                .Attribute(W + "val")?
                .Value;

            if (string.Equals(paragraphStyle, UsfmDocxContractV1.ParagraphStyles.Mt, StringComparison.Ordinal))
            {
                var text = ExtractNodeText(paragraph).Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    lines.Add($"\\mt {text}");
                }

                continue;
            }

            if (string.Equals(paragraphStyle, UsfmDocxContractV1.ParagraphStyles.Cl, StringComparison.Ordinal))
            {
                var text = ExtractNodeText(paragraph).Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    lines.Add($"\\cl {text}");
                }

                continue;
            }

            if (string.Equals(paragraphStyle, UsfmDocxContractV1.ParagraphStyles.P, StringComparison.Ordinal))
            {
                lines.Add("\\p");
                verseCount += ConvertUsfmParagraph(paragraph, footnotesById, mode, lines, issues);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(paragraphStyle))
            {
                unknownParagraphStyleCounts[paragraphStyle] = unknownParagraphStyleCounts.TryGetValue(paragraphStyle, out var count)
                    ? count + 1
                    : 1;
            }
        }

        return new BodyConversionResult(verseCount, unknownParagraphStyleCounts);
    }

    private static int ConvertUsfmParagraph(
        XElement paragraph,
        IReadOnlyDictionary<string, string> footnotesById,
        ContractMode mode,
        IList<string> lines,
        ICollection<ContractIssue> issues)
    {
        var verseCount = 0;
        string? currentVerseNumber = null;
        var verseText = new StringBuilder();

        foreach (var token in FlattenParagraphTokens(paragraph))
        {
            if (token.Kind == ParagraphTokenKind.VerseNumber)
            {
                if (FlushVerse(lines, ref currentVerseNumber, verseText))
                {
                    verseCount++;
                }

                var verseNumber = token.Value.Trim();
                if (string.IsNullOrWhiteSpace(verseNumber))
                {
                    issues.Add(Issue(mode, "VERSE_NUMBER_EMPTY", "Verse number run was empty."));
                }

                currentVerseNumber = verseNumber;
                continue;
            }

            if (token.Kind == ParagraphTokenKind.FootnoteReference)
            {
                if (currentVerseNumber is null)
                {
                    issues.Add(Issue(mode, "FOOTNOTE_WITHOUT_VERSE", "Footnote reference found before any verse number in paragraph."));
                    continue;
                }

                if (footnotesById.TryGetValue(token.Value, out var noteText) && !string.IsNullOrWhiteSpace(noteText))
                {
                    verseText.Append(" \\f + \\ft ");
                    verseText.Append(noteText.Trim());
                    verseText.Append(" \\f*");
                }
                else
                {
                    issues.Add(Issue(
                        mode,
                        "FOOTNOTE_MISSING_TEXT",
                        $"Footnote reference id '{token.Value}' has no corresponding footnote text."));
                }

                continue;
            }

            if (currentVerseNumber is null)
            {
                var hasText = !string.IsNullOrWhiteSpace(token.Value);
                if (hasText)
                {
                    issues.Add(Issue(mode, "VERSE_TEXT_WITHOUT_NUMBER", "Paragraph text found before any USFM_V verse run."));
                }

                continue;
            }

            verseText.Append(token.Value);
        }

        if (FlushVerse(lines, ref currentVerseNumber, verseText))
        {
            verseCount++;
        }

        return verseCount;
    }

    private static bool FlushVerse(IList<string> lines, ref string? currentVerseNumber, StringBuilder verseText)
    {
        if (currentVerseNumber is null)
        {
            verseText.Clear();
            return false;
        }

        lines.Add($"\\v {currentVerseNumber} {verseText.ToString().Trim()}");
        currentVerseNumber = null;
        verseText.Clear();
        return true;
    }

    private static List<string> ConvertBodyUsingHeuristics(
        XDocument documentXml,
        IReadOnlyDictionary<string, string> footnotesById,
        ContractMode mode,
        ICollection<ContractIssue> issues,
        bool preserveVerseMarkers,
        IReadOnlySet<string>? selectedBookIds)
    {
        var lines = new List<string>();
        var body = documentXml.Root?
            .Element(W + "body");

        if (body is null)
        {
            return lines;
        }

        var seenBookTitle = false;
        var seenCanonicalBookTitle = false;
        var seenChapter = false;
        string? currentBookTitle = null;
        var lastVerseLineIndex = -1;
        var lastVerseNumber = 0;
        var lastExplicitVerseLineIndex = -1;
        var lastVerseWasInferredFromBoundary = false;
        var paragraphOpen = false;
        var ignoredPrefaceCount = 0;
        int? pendingExplicitVerse = null;
        var selectedSingleBookId = selectedBookIds is { Count: 1 }
            ? selectedBookIds.First().Trim().ToUpperInvariant()
            : null;

        void ProcessText(string text, bool isAutoNumberedVerseCandidate)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (TrySplitInlineBookTitle(text, out var before, out var marker, out var after))
            {
                if (!string.IsNullOrWhiteSpace(before))
                {
                    ProcessText(before, false);
                }

                seenBookTitle = true;
                seenCanonicalBookTitle = true;
                currentBookTitle = marker;
                seenChapter = false;
                paragraphOpen = false;
                lastVerseLineIndex = -1;
                lastVerseNumber = 0;
                lastExplicitVerseLineIndex = -1;
                lastVerseWasInferredFromBoundary = false;
                lines.Add($"\\mt {marker}");

                if (!string.IsNullOrWhiteSpace(after))
                {
                    ProcessText(after, false);
                }

                return;
            }

            if (TrySplitBookTitleWithInlineChapter(text, out var inlineBookTitle, out var inlineChapterLine, out var inlineChapterNumber))
            {
                seenBookTitle = true;
                seenCanonicalBookTitle = true;
                currentBookTitle = inlineBookTitle;
                seenChapter = true;
                paragraphOpen = false;
                lastVerseLineIndex = -1;
                lastVerseNumber = 0;
                lastExplicitVerseLineIndex = -1;
                lastVerseWasInferredFromBoundary = false;
                pendingExplicitVerse = null;
                lines.Add($"\\mt {inlineBookTitle}");
                lines.Add($"\\c {inlineChapterNumber}");
                lines.Add($"\\cl {NormalizeChapterDisplayLine(inlineChapterLine)}");
                return;
            }

            if (IsBookTitleText(text) || IsNumericBookTitleText(text))
            {
                if (seenCanonicalBookTitle &&
                    !string.IsNullOrWhiteSpace(currentBookTitle) &&
                    string.Equals(NormalizeHeadingText(text), NormalizeHeadingText(currentBookTitle), StringComparison.Ordinal))
                {
                    return;
                }

                seenBookTitle = true;
                seenCanonicalBookTitle = true;
                currentBookTitle = text;
                seenChapter = false;
                paragraphOpen = false;
                lastVerseLineIndex = -1;
                lastVerseNumber = 0;
                lastExplicitVerseLineIndex = -1;
                lastVerseWasInferredFromBoundary = false;
                lines.Add($"\\mt {text}");
                return;
            }

            if (seenCanonicalBookTitle && !seenChapter && IsIgnorablePreChapterHeaderLine(text))
            {
                return;
            }

            if (TrySplitEmbeddedChapterHeading(text, out var chapterBefore, out var embeddedChapterLine, out var embeddedChapterNumber, out var chapterAfter))
            {
                if (!string.IsNullOrWhiteSpace(chapterBefore))
                {
                    ProcessText(chapterBefore, false);
                }

                seenChapter = true;
                paragraphOpen = false;
                lastVerseLineIndex = -1;
                lastVerseNumber = 0;
                lastExplicitVerseLineIndex = -1;
                lastVerseWasInferredFromBoundary = false;
                pendingExplicitVerse = null;
                lines.Add($"\\c {embeddedChapterNumber}");
                lines.Add($"\\cl {NormalizeChapterDisplayLine(embeddedChapterLine)}");

                if (!string.IsNullOrWhiteSpace(chapterAfter))
                {
                    ProcessText(chapterAfter, false);
                }

                return;
            }

            var chapterMatch = ChapterRegex.Match(text);
            if (!seenCanonicalBookTitle
                && chapterMatch.Success
                && !string.IsNullOrWhiteSpace(selectedSingleBookId))
            {
                seenBookTitle = true;
                seenCanonicalBookTitle = true;
                currentBookTitle = BookIdToDisplayTitle(selectedSingleBookId);
                lines.Add($"\\mt {currentBookTitle}");
                issues.Add(Issue(
                    mode,
                    "BOOK_HEADING_INFERRED_FROM_SELECTION",
                    $"DOCX starts with a chapter heading; used selected book {selectedSingleBookId} as the book heading."));
            }

            if (!seenCanonicalBookTitle)
            {
                ignoredPrefaceCount++;
                return;
            }

            if (chapterMatch.Success && TryParseVerseOrChapterNumber(chapterMatch.Groups[1].Value, out var parsedChapterNumber))
            {
                seenChapter = true;
                paragraphOpen = false;
                lastVerseLineIndex = -1;
                lastVerseNumber = 0;
                lastExplicitVerseLineIndex = -1;
                lastVerseWasInferredFromBoundary = false;
                pendingExplicitVerse = null;
                lines.Add($"\\c {parsedChapterNumber}");
                lines.Add($"\\cl {NormalizeChapterDisplayLine(text)}");
                return;
            }

            if (TryParseUsfmVerseMarkerOnly(text, out var explicitVerseNumber))
            {
                pendingExplicitVerse = explicitVerseNumber;
                return;
            }

            if (TryParsePlainVerseMarkerOnly(text, out var plainExplicitVerseNumber))
            {
                pendingExplicitVerse = plainExplicitVerseNumber;
                return;
            }

            if (TryParseUsfmVerseInline(text, out var inlineVerseNumber, out var inlineVerseText))
            {
                if (TryReconcileRepeatedExplicitVerseAfterInference(
                    inlineVerseNumber,
                    ref inlineVerseText,
                    lines,
                    ref lastVerseLineIndex,
                    ref lastVerseNumber,
                    ref lastExplicitVerseLineIndex,
                    ref lastVerseWasInferredFromBoundary))
                {
                    // reconciled prior inferred verse into the preceding explicit verse
                }

                if (!paragraphOpen)
                {
                    lines.Add("\\p");
                    paragraphOpen = true;
                }

                var splitVerses = SplitExplicitUsfmVerseText(
                    inlineVerseNumber,
                    inlineVerseText,
                    out var trailingVerseMarker);
                foreach (var splitVerse in splitVerses)
                {
                    lines.Add($"\\v {splitVerse.Number} {CleanVerseBodyLead(splitVerse.Text)}");
                    lastVerseLineIndex = lines.Count - 1;
                    lastVerseNumber = splitVerse.Number;
                    lastExplicitVerseLineIndex = lastVerseLineIndex;
                    lastVerseWasInferredFromBoundary = false;
                }
                pendingExplicitVerse = trailingVerseMarker;
                return;
            }

            if (pendingExplicitVerse is int pendingVerseNumber)
            {
                if (!paragraphOpen)
                {
                    lines.Add("\\p");
                    paragraphOpen = true;
                }

                var pendingVerseText = SanitizePendingVerseText(text, pendingVerseNumber);
                if (TryReconcileRepeatedExplicitVerseAfterInference(
                    pendingVerseNumber,
                    ref pendingVerseText,
                    lines,
                    ref lastVerseLineIndex,
                    ref lastVerseNumber,
                    ref lastExplicitVerseLineIndex,
                    ref lastVerseWasInferredFromBoundary))
                {
                    // reconciled prior inferred verse into the preceding explicit verse
                }
                var splitVerses = SplitExplicitUsfmVerseText(
                    pendingVerseNumber,
                    pendingVerseText,
                    out var trailingVerseMarker);
                foreach (var splitVerse in splitVerses)
                {
                    lines.Add($"\\v {splitVerse.Number} {CleanVerseBodyLead(splitVerse.Text)}");
                    lastVerseLineIndex = lines.Count - 1;
                    lastVerseNumber = splitVerse.Number;
                    lastExplicitVerseLineIndex = lastVerseLineIndex;
                    lastVerseWasInferredFromBoundary = false;
                }
                pendingExplicitVerse = trailingVerseMarker;
                return;
            }

            if (preserveVerseMarkers && TryParseLeadingVerse(text, out var parsedVerseNumber, out var parsedVerseText))
            {
                if (!paragraphOpen)
                {
                    lines.Add("\\p");
                    paragraphOpen = true;
                }

                var splitVerses = SplitMergedVerseText(parsedVerseNumber, parsedVerseText);
                foreach (var splitVerse in splitVerses)
                {
                    lines.Add($"\\v {splitVerse.Number} {CleanVerseBodyLead(splitVerse.Text)}");
                    lastVerseLineIndex = lines.Count - 1;
                    lastVerseNumber = splitVerse.Number;
                    lastExplicitVerseLineIndex = lastVerseLineIndex;
                    lastVerseWasInferredFromBoundary = false;
                }

                if (splitVerses.Count > 1)
                {
                    issues.Add(Issue(
                        mode,
                        "VERSE_SPLIT_RECOVERED",
                        $"Recovered {splitVerses.Count - 1} merged verse marker(s) in chapter text."));
                }
                return;
            }

            var verseMatch = VerseRegex.Match(text);
            if (!verseMatch.Success && preserveVerseMarkers)
            {
                var loose = VerseRegexLoose.Match(text);
                if (loose.Success && loose.Groups[2].Value.Length > 0)
                {
                    verseMatch = loose;
                }
            }
            if (verseMatch.Success)
            {
                if (!paragraphOpen)
                {
                    lines.Add("\\p");
                    paragraphOpen = true;
                }

                if (!TryParseVerseOrChapterNumber(verseMatch.Groups[1].Value, out var verseNumber))
                {
                    return;
                }
                var verseText = preserveVerseMarkers
                    ? verseMatch.Groups[2].Value.Trim()
                    : NormalizeVerseLead(verseMatch.Groups[2].Value).Trim();

                if (preserveVerseMarkers)
                {
                    var splitVerses = SplitMergedVerseText(verseNumber, verseText);
                    foreach (var splitVerse in splitVerses)
                    {
                        lines.Add($"\\v {splitVerse.Number} {CleanVerseBodyLead(splitVerse.Text)}");
                        lastVerseLineIndex = lines.Count - 1;
                        lastVerseNumber = splitVerse.Number;
                        lastExplicitVerseLineIndex = lastVerseLineIndex;
                        lastVerseWasInferredFromBoundary = false;
                    }

                    if (splitVerses.Count > 1)
                    {
                        issues.Add(Issue(
                            mode,
                            "VERSE_SPLIT_RECOVERED",
                            $"Recovered {splitVerses.Count - 1} merged verse marker(s) in chapter text."));
                    }
                }
                else
                {
                    var splitVerses = SplitMergedVerseText(verseNumber, verseText);

                    foreach (var splitVerse in splitVerses)
                    {
                        lines.Add($"\\v {splitVerse.Number} {CleanVerseBodyLead(splitVerse.Text)}");
                        lastVerseLineIndex = lines.Count - 1;
                        lastVerseNumber = splitVerse.Number;
                        lastExplicitVerseLineIndex = lastVerseLineIndex;
                        lastVerseWasInferredFromBoundary = false;
                    }

                    if (splitVerses.Count > 1)
                    {
                        issues.Add(Issue(
                            mode,
                            "VERSE_SPLIT_RECOVERED",
                            $"Recovered {splitVerses.Count - 1} merged verse marker(s) in chapter text."));
                    }
                }

                return;
            }

            if (!preserveVerseMarkers && seenChapter && isAutoNumberedVerseCandidate && lastVerseNumber > 0)
            {
                if (!paragraphOpen)
                {
                    lines.Add("\\p");
                    paragraphOpen = true;
                }

                var inferredVerse = lastVerseNumber + 1;
                lines.Add($"\\v {inferredVerse} {text}");
                lastVerseLineIndex = lines.Count - 1;
                lastVerseNumber = inferredVerse;
                issues.Add(Issue(
                    mode,
                    "VERSE_INFERRED_FROM_LIST_NUMBERING",
                    $"Inferred verse {inferredVerse} from DOCX list numbering."));
                return;
            }

            if (!seenChapter)
            {
                if (!seenBookTitle)
                {
                    lines.Add($"\\mt {text}");
                    seenBookTitle = true;
                }
                else
                {
                    lines.Add($"\\cl {text}");
                }

                return;
            }

            if (lastVerseLineIndex >= 0)
            {
                if (preserveVerseMarkers && ShouldInferNextVerseFromParagraph(text, lastVerseNumber, lines[lastVerseLineIndex]))
                {
                var inferredVerse = lastVerseNumber + 1;
                lines.Add($"\\v {inferredVerse} {text}");
                lastVerseLineIndex = lines.Count - 1;
                lastVerseNumber = inferredVerse;
                lastVerseWasInferredFromBoundary = true;
                issues.Add(Issue(
                    mode,
                    "VERSE_INFERRED_FROM_PARAGRAPH_BOUNDARY",
                        $"Inferred verse {inferredVerse} from paragraph boundary after verse {inferredVerse - 1}."));
                }
                else
                {
                    lines[lastVerseLineIndex] = $"{lines[lastVerseLineIndex]} {text}";
                }
                return;
            }

            if (seenChapter && !paragraphOpen)
            {
                lines.Add("\\p");

                if (preserveVerseMarkers)
                {
                    lines.Add($"\\d {text}");
                    paragraphOpen = true;
                    lastVerseLineIndex = -1;
                    lastVerseNumber = 0;
                    lastExplicitVerseLineIndex = -1;
                    lastVerseWasInferredFromBoundary = false;
                }
                else
                {
                    lines.Add($"\\v 1 {text}");
                    paragraphOpen = true;
                    lastVerseLineIndex = lines.Count - 1;
                    lastVerseNumber = 1;
                    lastExplicitVerseLineIndex = lastVerseLineIndex;
                    lastVerseWasInferredFromBoundary = false;
                    issues.Add(Issue(
                        mode,
                        "VERSE_INFERRED",
                        $"Inferred verse 1 for unnumbered paragraph: '{text}'"));
                }
                return;
            }

            issues.Add(Issue(
                mode,
                "PARAGRAPH_UNMAPPED",
                $"Could not map paragraph text after chapter detection: '{text}'"));
        }

        foreach (var paragraph in body.Elements(W + "p"))
        {
            var text = NormalizeWhitespace(BuildParagraphTextWithFootnotes(paragraph, footnotesById, mode, issues)).Trim();
            var isAutoNumberedVerseCandidate = IsNumberedListParagraph(paragraph);
            ProcessText(text, isAutoNumberedVerseCandidate);
        }

        if (ignoredPrefaceCount > 0)
        {
            issues.Add(Issue(
                mode,
                "PREFACE_IGNORED",
                $"Ignored {ignoredPrefaceCount} preface paragraph(s) before first detected book heading."));
        }

        return lines;
    }

    private static bool IsNumberedListParagraph(XElement paragraph)
    {
        var pPr = paragraph.Element(W + "pPr");
        var numPr = pPr?.Element(W + "numPr");
        if (numPr is null)
        {
            return false;
        }

        var numId = (string?)numPr.Element(W + "numId")?.Attribute(W + "val");
        return !string.IsNullOrWhiteSpace(numId) && !string.Equals(numId, "0", StringComparison.Ordinal);
    }


    private static string BookIdToDisplayTitle(string bookId)
    {
        return bookId.ToUpperInvariant() switch
        {
            "GEN" => "Genesis", "EXO" => "Exodus", "LEV" => "Leviticus", "NUM" => "Numbers", "DEU" => "Deuteronomy",
            "JOS" => "Joshua", "JDG" => "Judges", "RUT" => "Ruth", "1SA" => "1 Samuel", "2SA" => "2 Samuel",
            "1KI" => "1 Kings", "2KI" => "2 Kings", "1CH" => "1 Chronicles", "2CH" => "2 Chronicles",
            "EZR" => "Ezra", "NEH" => "Nehemiah", "EST" => "Esther", "JOB" => "Job", "PSA" => "Psalms",
            "PRO" => "Proverbs", "ECC" => "Ecclesiastes", "SNG" => "Song of Songs", "ISA" => "Isaiah",
            "JER" => "Jeremiah", "LAM" => "Lamentations", "EZK" => "Ezekiel", "DAN" => "Daniel",
            "HOS" => "Hosea", "JOL" => "Joel", "AMO" => "Amos", "OBA" => "Obadiah", "JON" => "Jonah",
            "MIC" => "Micah", "NAM" => "Nahum", "HAB" => "Habakkuk", "ZEP" => "Zephaniah", "HAG" => "Haggai",
            "ZEC" => "Zechariah", "MAL" => "Malachi", "MAT" => "Matthew", "MRK" => "Mark", "LUK" => "Luke",
            "JHN" => "John", "ACT" => "Acts", "ROM" => "Romans", "1CO" => "1 Corinthians", "2CO" => "2 Corinthians",
            "GAL" => "Galatians", "EPH" => "Ephesians", "PHP" => "Philippians", "COL" => "Colossians",
            "1TH" => "1 Thessalonians", "2TH" => "2 Thessalonians", "1TI" => "1 Timothy", "2TI" => "2 Timothy",
            "TIT" => "Titus", "PHM" => "Philemon", "HEB" => "Hebrews", "JAS" => "James", "1PE" => "1 Peter",
            "2PE" => "2 Peter", "1JN" => "1 John", "2JN" => "2 John", "3JN" => "3 John", "JUD" => "Jude",
            "REV" => "Revelation",
            _ => bookId
        };
    }

    private static bool TrySplitInlineBookTitle(string text, out string before, out string title, out string after)
    {
        before = string.Empty;
        title = string.Empty;
        after = string.Empty;

        var match = InlineBookMarkerRegex.Match(text);
        if (!match.Success)
        {
            return false;
        }

        before = match.Groups["before"].Value.Trim();
        title = match.Groups["title"].Value.Trim();
        after = match.Groups["after"].Value.Trim();

        if (!IsBookTitleText(title))
        {
            return false;
        }

        return true;
    }

    private static bool TrySplitEmbeddedChapterHeading(string text, out string before, out string chapterLine, out int chapterNumber, out string after)
    {
        before = string.Empty;
        chapterLine = string.Empty;
        chapterNumber = 0;
        after = string.Empty;

        var match = Regex.Match(
            text,
            @"^(?<before>.+?)\s+(?<chapterLine>(?:[\p{L}\p{M}]+\s+){0,4}(?:Глава|Chapter|باب)\s*(?<chapter>[0-9\u0660-\u0669\u06F0-\u06F9]+))\s+(?<after>(?:\\[vV]\s*[0-9\u0660-\u0669\u06F0-\u06F9]|[0-9\u0660-\u0669\u06F0-\u06F9]{1,3}\s*[.)۔:]).*)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        if (!match.Success || !TryParseVerseOrChapterNumber(match.Groups["chapter"].Value, out chapterNumber))
        {
            return false;
        }

        before = match.Groups["before"].Value.Trim();
        chapterLine = match.Groups["chapterLine"].Value.Trim();
        after = match.Groups["after"].Value.Trim();
        return !string.IsNullOrWhiteSpace(before)
            && !string.IsNullOrWhiteSpace(chapterLine)
            && !string.IsNullOrWhiteSpace(after);
    }

    private static bool TrySplitBookTitleWithInlineChapter(string text, out string title, out string chapterLine, out int chapterNumber)
    {
        title = string.Empty;
        chapterLine = string.Empty;
        chapterNumber = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = BookTitleWithInlineChapterRegex.Match(text);
        if (!match.Success)
        {
            return false;
        }

        var candidateTitle = match.Groups["title"].Value.Trim();
        if (!(IsBookTitleText(candidateTitle) || IsNumericBookTitleText(candidateTitle)))
        {
            return false;
        }

        if (!TryParseVerseOrChapterNumber(match.Groups["chapter"].Value, out chapterNumber))
        {
            return false;
        }

        title = candidateTitle;
        chapterLine = $"{match.Groups["keyword"].Value.Trim()} {match.Groups["chapter"].Value.Trim()}";
        return true;
    }

    private static bool IsBookTitleText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        if (ChapterRegex.IsMatch(text))
        {
            return false;
        }

        if (BookTitleRegex.IsMatch(text))
        {
            return true;
        }

        if (PlainBookTitleRegex.IsMatch(text))
        {
            return true;
        }

        if (StandaloneBookTitleRegex.IsMatch(text))
        {
            return true;
        }

        if (EquivalentBookTitleRegex.IsMatch(text))
        {
            return true;
        }

        if (IsAliasOnlyBookHeadingCandidate(text) && InferBookIdFromProfile(text) is not null)
        {
            return true;
        }

        return false;
    }

    private static bool IsAliasOnlyBookHeadingCandidate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (ChapterRegex.IsMatch(text))
        {
            return false;
        }

        if (text.IndexOfAny(new[] { '.', '!', '?', ':', ';', ',', '"', '۔', '\\' }) >= 0)
        {
            return false;
        }

        var words = Regex.Matches(text, @"[\p{L}\p{M}]+").Count;
        if (words is 0 or > 5)
        {
            return false;
        }

        return text.Trim().Length <= 40;
    }

    private static bool IsNumericBookTitleText(string text)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return false;
    }

    var normalized = NormalizeIndicDigits(text);
    if (!NumericBookTitleRegex.IsMatch(normalized)
        && !Regex.IsMatch(
            normalized,
            @"^(?:\d+|I|II|III|IV)\s*[.\-۔]?\s*(?:Книга\s+|Book\s+)?[\p{L}\p{M}][\p{L}\p{M}\s\-]{1,80}$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
    {
        return false;
    }

    if (ChapterRegex.IsMatch(text))
    {
        return false;
    }

    // Avoid misclassifying ordinary verse text as headings.
    if (text.IndexOfAny(new[] { '.', '!', '?', ':', ';', ',', '"' }) >= 0)
    {
        return false;
    }

    var trimmed = normalized.TrimStart();
    var numericPrefix = Regex.Match(trimmed, "^(\\d+)");
    if (numericPrefix.Success
        && int.TryParse(numericPrefix.Groups[1].Value, out var numericBook)
        && numericBook > 4)
    {
        return false;
    }
    return true;
}

    private static bool IsIgnorablePreChapterHeaderLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (GenericTranslationLabelRegex.IsMatch(text))
        {
            return true;
        }

        if (TryParseVerseOrChapterNumber(text, out _))
        {
            return false;
        }

        var words = Regex.Matches(text, @"[\p{L}\p{M}]+").Count;
        if (words is < 2 or > 6)
        {
            return false;
        }

        if (text.Contains('،') || text.Contains(',') || text.Contains('.') || text.Contains('۔'))
        {
            return false;
        }

        return TranslatorCreditRegex.IsMatch(text);
    }

    private static string? InferBookIdFromProfile(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var lowered = title.ToLowerInvariant();
        var normalizedTitle = NormalizeForAliasMatch(title);

        foreach (var kvp in ProfileContext.Current.BookAliases)
        {
            foreach (var alias in kvp.Value)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                if (lowered.Contains(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Key;
                }

                var normalizedAlias = NormalizeForAliasMatch(alias);
                if (normalizedAlias.Length > 0
                    && normalizedTitle.Contains(normalizedAlias, StringComparison.Ordinal))
                {
                    return kvp.Key;
                }
            }
        }

        return null;
    }

    private static string NormalizeForAliasMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in NormalizeIndicDigits(value))
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

private static List<VerseSegment> SplitMergedVerseText
(int firstVerseNumber, string verseText)
    {
        var segments = new List<VerseSegment>();

        var currentVerse = firstVerseNumber;
        var currentStart = SkipDuplicatedVerseMarker(verseText, 0, firstVerseNumber);
        var index = currentStart;

        while (index < verseText.Length)
        {
            var markerBoundaryStart = index;
            if (TrySkipUsfmVerseMarkerPrefix(verseText, index, out var markerNumberStart)
                && markerNumberStart < verseText.Length
                && IsVerseNumberDigit(verseText[markerNumberStart]))
            {
                index = markerNumberStart;
            }
            else if (!IsVerseNumberDigit(verseText[index]))
            {
                index++;
                continue;
            }

            if (!TryReadVerseNumberAt(verseText, index, out var candidateVerse, out var end))
            {
                index++;
                continue;
            }

            if (candidateVerse != currentVerse + 1)
            {
                index++;
                continue;
            }

            var markerEnd = SkipDuplicatedVerseMarker(verseText, end, candidateVerse);
            var hasDuplicatedMarkerBoundary = markerEnd > end;
            var hasTailDigitPrefix = TryFindLeakedTailDigitBeforeMarker(verseText, index, candidateVerse, out var pieceEnd);
            var hasUsfmMarkerBoundary = markerBoundaryStart < index;
            var hasInlineBoundary = hasDuplicatedMarkerBoundary || hasTailDigitPrefix || hasUsfmMarkerBoundary || IsInlineVerseBoundary(verseText, index);
            var hasDotBoundary = IsDotDelimitedBoundary(verseText, index, end);
            if (!hasInlineBoundary && !hasDotBoundary)
            {
                index++;
                continue;
            }

            var boundaryEnd = hasTailDigitPrefix ? pieceEnd : hasUsfmMarkerBoundary ? markerBoundaryStart : index;
            var piece = verseText[currentStart..boundaryEnd].Trim();
            if (!string.IsNullOrWhiteSpace(piece))
            {
                segments.Add(new VerseSegment(currentVerse, piece));
            }

            currentVerse = candidateVerse;
            currentStart = hasDuplicatedMarkerBoundary ? markerEnd : hasDotBoundary ? end + 1 : end;
            if (hasTailDigitPrefix)
            {
                currentStart = SkipMarkerArtifacts(verseText, currentStart);
            }
            else if (!hasDuplicatedMarkerBoundary)
            {
                currentStart = SkipMarkerArtifacts(verseText, currentStart);
            }

            index = currentStart;
        }

        var tail = verseText[currentStart..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            segments.Add(new VerseSegment(currentVerse, tail));
        }

        if (segments.Count == 0)
        {
            segments.Add(new VerseSegment(firstVerseNumber, verseText.Trim()));
            return segments;
        }

        if (segments.Count == 1 && TrySplitByNextVerseFallback(verseText, firstVerseNumber, out var fallback))
        {
            return fallback;
        }

        return segments;
    }

    private static List<VerseSegment> SplitExplicitUsfmVerseText(
        int firstVerseNumber,
        string verseText,
        out int? trailingVerseMarker)
    {
        var segments = new List<VerseSegment>();
        var currentVerse = firstVerseNumber;
        var currentStart = 0;
        var markerCount = 0;
        trailingVerseMarker = null;

        foreach (Match match in EmbeddedUsfmVerseMarkerRegex.Matches(verseText))
        {
            if (!TryParseVerseOrChapterNumber(match.Groups[1].Value, out var nextVerse))
            {
                continue;
            }

            markerCount++;
            var currentText = verseText[currentStart..match.Index].Trim();
            if (!string.IsNullOrWhiteSpace(currentText))
            {
                segments.Add(new VerseSegment(currentVerse, currentText));
            }

            currentVerse = nextVerse;
            currentStart = match.Index + match.Length;
        }

        var tail = verseText[currentStart..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            segments.Add(new VerseSegment(currentVerse, tail));
        }
        else if (markerCount > 0)
        {
            trailingVerseMarker = currentVerse;
        }

        if (segments.Count == 0 && trailingVerseMarker is null)
        {
            segments.Add(new VerseSegment(firstVerseNumber, verseText.Trim()));
        }

        return segments;
    }

    private static bool TryReadVerseNumberAt(string text, int start, out int verseNumber, out int end)
    {
        verseNumber = 0;
        end = start;
        var digitScript = GetVerseDigitScript(start < text.Length ? text[start] : '\0');
        if (digitScript == 0)
        {
            return false;
        }

        while (end < text.Length
               && IsVerseNumberDigit(text[end])
               && GetVerseDigitScript(text[end]) == digitScript
               && end - start < 3)
        {
            end++;
        }

        return end > start && TryParseVerseOrChapterNumber(text[start..end], out verseNumber);
    }

    private static int GetVerseDigitScript(char value)
    {
        if (value is >= '0' and <= '9')
        {
            return 1;
        }

        if (value is >= '\u0660' and <= '\u0669')
        {
            return 2;
        }

        if (value is >= '\u06F0' and <= '\u06F9')
        {
            return 3;
        }

        return 0;
    }

    private static bool IsVerseNumberDigit(char value)
    {
        return value is >= '0' and <= '9'
            || value is >= '\u0660' and <= '\u0669'
            || value is >= '\u06F0' and <= '\u06F9';
    }


    private static bool TrySkipUsfmVerseMarkerPrefix(string text, int start, out int afterPrefix)
    {
        afterPrefix = start;
        var current = start;
        while (current < text.Length && char.IsWhiteSpace(text[current]))
        {
            current++;
        }

        if (current + 1 >= text.Length
            || text[current] != '\\'
            || (text[current + 1] != 'v' && text[current + 1] != 'V'))
        {
            return false;
        }

        current += 2;
        while (current < text.Length && char.IsWhiteSpace(text[current]))
        {
            current++;
        }

        afterPrefix = current;
        return true;
    }

    private static int SkipDuplicatedVerseMarker(string text, int start, int verseNumber)
    {
        var current = start;
        while (current < text.Length && char.IsWhiteSpace(text[current]))
        {
            current++;
        }

        if (TrySkipUsfmVerseMarkerPrefix(text, current, out var afterUsfmPrefix))
        {
            current = afterUsfmPrefix;
        }

        if (!TryReadVerseNumberAt(text, current, out var duplicateVerse, out var afterDuplicate))
        {
            return start;
        }

        var afterMarker = afterDuplicate;
        if (verseNumber >= 10
            && duplicateVerse == verseNumber % 10
            && current == start
            && afterDuplicate == current + 1)
        {
            // Some Urdu/Arabic-script files render a duplicate localized tail digit without spacing,
            // e.g. "18۸۔". Treat the localized tail as marker artifact, not verse text.
        }
        else if (duplicateVerse != verseNumber)
        {
            return start;
        }

        while (afterMarker < text.Length && char.IsWhiteSpace(text[afterMarker]))
        {
            afterMarker++;
        }

        if (afterMarker < text.Length && text[afterMarker] is '.' or ')' or '۔' or ':')
        {
            afterMarker++;
        }

        while (afterMarker < text.Length && char.IsWhiteSpace(text[afterMarker]))
        {
            afterMarker++;
        }

        return afterMarker;
    }

    private static bool IsDotDelimitedBoundary(string text, int index, int end)
    {
        if (end >= text.Length || text[end] != '.')
        {
            return false;
        }

        if (index > 0 && char.IsDigit(text[index - 1]))
        {
            return false;
        }

        if (end + 1 >= text.Length)
        {
            return true;
        }

        var next = text[end + 1];
        return char.IsWhiteSpace(next) || next is '"' or '\'' or '«' or '“' || char.IsLetter(next);
    }

    private static bool TrySplitByNextVerseFallback(
        string verseText,
        int firstVerseNumber,
        out List<VerseSegment> segments)
    {
        segments = new List<VerseSegment>();
        var needle = (firstVerseNumber + 1).ToString();
        var searchFrom = 0;

        while (searchFrom < verseText.Length)
        {
            var index = verseText.IndexOf(needle, searchFrom, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            if (index > 0 && IsInlineVerseBoundary(verseText, index))
            {
                var after = index + needle.Length;
                after = SkipMarkerArtifacts(verseText, after);

                var left = verseText[..index].Trim();
                var right = verseText[after..].Trim();
                if (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
                {
                    segments.Add(new VerseSegment(firstVerseNumber, left));
                    segments.Add(new VerseSegment(firstVerseNumber + 1, right));
                    return true;
                }
            }

            searchFrom = index + 1;
        }

        return false;
    }


    private static bool TryFindLeakedTailDigitBeforeMarker(string text, int markerIndex, int verseNumber, out int pieceEnd)
    {
        pieceEnd = markerIndex;
        if (verseNumber < 10 || markerIndex <= 0)
        {
            return false;
        }

        var i = markerIndex - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
        {
            i--;
        }

        if (i < 0 || !IsVerseNumberDigit(text[i]))
        {
            return false;
        }

        if (!TryParseVerseOrChapterNumber(text[i].ToString(), out var tailDigit)
            || tailDigit != verseNumber % 10)
        {
            return false;
        }

        var beforeTail = i - 1;
        while (beforeTail >= 0 && char.IsWhiteSpace(text[beforeTail]))
        {
            beforeTail--;
        }

        if (beforeTail >= 0 && !IsBoundaryPunctuation(text[beforeTail]))
        {
            return false;
        }

        pieceEnd = i;
        return true;
    }

    private static bool IsInlineVerseBoundary(string text, int index)
    {
        if (index <= 0 || index >= text.Length)
        {
            return false;
        }

        var prev = text[index - 1];
        if (char.IsDigit(prev))
        {
            return false;
        }

        if (IsBoundaryPunctuation(prev))
        {
            return true;
        }

        if (!char.IsWhiteSpace(prev))
        {
            return false;
        }

        var i = index - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
        {
            i--;
        }

        if (i < 0)
        {
            return true;
        }

        return IsBoundaryPunctuation(text[i]);
    }

    private static int SkipMarkerArtifacts(string text, int start)
    {
        var current = start;
        if (current < text.Length && char.IsLetter(text[current]))
        {
            var first = text[current];
            var secondExists = current + 1 < text.Length && char.IsLetter(text[current + 1]);
            var thirdExists = current + 2 < text.Length && char.IsLetter(text[current + 2]);

            if (secondExists && thirdExists && char.ToLowerInvariant(first) == char.ToLowerInvariant(text[current + 1]))
            {
                // Example: "43Пполовина" -> drop first duplicated marker letter, keep "половина".
                current += 1;
            }
            else
            {
                var consumed = 0;
                while (current < text.Length && char.IsLetter(text[current]) && consumed < 3)
                {
                    current++;
                    consumed++;
                }
            }
        }

        while (current < text.Length && char.IsWhiteSpace(text[current]))
        {
            current++;
        }

        return current;
    }

    private static string NormalizeVerseLead(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return raw;
        }

        var value = raw;
        var i = 0;
        while (i < value.Length && char.IsWhiteSpace(value[i]))
        {
            i++;
        }

        if (i + 1 < value.Length && char.IsLetter(value[i]) && char.IsLetter(value[i + 1]))
        {
            var first = value[i];
            var second = value[i + 1];
            var sameLetter = char.ToLowerInvariant(first) == char.ToLowerInvariant(second);

            if (sameLetter)
            {
                if (i + 2 < value.Length && char.IsWhiteSpace(value[i + 2]))
                {
                    // "33Аа сыновья..." -> drop "Аа "
                    i += 3;
                }
                else if (i + 2 < value.Length && char.IsLetter(value[i + 2]))
                {
                    // "43Пполовина..." -> keep uppercase initial and drop duplicated lowercase.
                    value = value[..(i + 1)] + value[(i + 2)..];
                }
            }
        }

        while (i < value.Length && char.IsWhiteSpace(value[i]))
        {
            i++;
        }

        return value[i..];
    }

    private static string SanitizePendingVerseText(string text, int verseNumber)
    {
        if (string.IsNullOrWhiteSpace(text) || verseNumber <= 0)
        {
            return text;
        }

        var fullMatch = Regex.Match(
            text,
            @"^\s*([0-9\u0660-\u0669\u06F0-\u06F9]{1,3})\s*[.)۔:]?\s+(.+)$",
            RegexOptions.CultureInvariant);

        if (fullMatch.Success
            && TryParseVerseOrChapterNumber(fullMatch.Groups[1].Value, out var parsedLeadingVerseNumber)
            && parsedLeadingVerseNumber == verseNumber)
        {
            return fullMatch.Groups[2].Value.Trim();
        }

        // Some RTL DOCX runs leak only the trailing digit of a standalone marker into the verse text.
        // Example: marker "\v 18" followed by text rendered as "٨ تاں ..." instead of "تاں ...".
        if (verseNumber >= 10)
        {
            var tailDigitMatch = Regex.Match(
                text,
                @"^\s*([0-9\u0660-\u0669\u06F0-\u06F9])\s+(.+)$",
                RegexOptions.CultureInvariant);

            if (tailDigitMatch.Success
                && TryParseVerseOrChapterNumber(tailDigitMatch.Groups[1].Value, out var leakedTailDigit)
                && leakedTailDigit == verseNumber % 10)
            {
                return tailDigitMatch.Groups[2].Value.Trim();
            }
        }

        return text.Trim();
    }

    private static bool TryReconcileRepeatedExplicitVerseAfterInference(
        int explicitVerseNumber,
        ref string explicitVerseText,
        IList<string> lines,
        ref int lastVerseLineIndex,
        ref int lastVerseNumber,
        ref int lastExplicitVerseLineIndex,
        ref bool lastVerseWasInferredFromBoundary)
    {
        if (!lastVerseWasInferredFromBoundary
            || lastVerseLineIndex < 0
            || lastExplicitVerseLineIndex < 0
            || lastExplicitVerseLineIndex >= lastVerseLineIndex
            || explicitVerseNumber != lastVerseNumber
            || explicitVerseNumber <= 0)
        {
            return false;
        }

        var inferredLine = lines[lastVerseLineIndex];
        var inferredMatch = Regex.Match(
            inferredLine,
            @"^\\v\s*([0-9\u0660-\u0669\u06F0-\u06F9]{1,3})\s*[\\.)۔:]?\s+(.*)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        if (!inferredMatch.Success
            || !TryParseVerseOrChapterNumber(inferredMatch.Groups[1].Value, out var parsedInferredVerse)
            || parsedInferredVerse != explicitVerseNumber)
        {
            return false;
        }

        var inferredText = inferredMatch.Groups[2].Value.Trim();
        if (!string.IsNullOrWhiteSpace(inferredText))
        {
            lines[lastExplicitVerseLineIndex] = $"{lines[lastExplicitVerseLineIndex]} {inferredText}".TrimEnd();
        }

        lines.RemoveAt(lastVerseLineIndex);
        lastVerseLineIndex = lastExplicitVerseLineIndex;
        lastVerseNumber = explicitVerseNumber - 1;
        lastVerseWasInferredFromBoundary = false;
        return true;
    }

    private static bool ShouldInferNextVerseFromParagraph(string text, int lastVerseNumber, string previousVerseLine)
    {
        if (lastVerseNumber <= 0 || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.TrimStart();
        if (trimmed.Length < 8)
        {
            return false;
        }

        if (VerseRegex.IsMatch(trimmed) || VerseRegexLoose.IsMatch(trimmed))
        {
            return false;
        }

        var lead = trimmed[0];
        if (!char.IsLetter(lead) && lead is not '"' and not '\'' and not '«' and not '“' and not '[' and not '(')
        {
            return false;
        }

        var previousText = previousVerseLine;
        var markerIndex = previousText.IndexOf(' ');
        if (markerIndex > 0 && previousText.StartsWith("\\v ", StringComparison.OrdinalIgnoreCase))
        {
            var secondSpace = previousText.IndexOf(' ', markerIndex + 1);
            if (secondSpace > 0 && secondSpace + 1 < previousText.Length)
            {
                previousText = previousText[(secondSpace + 1)..];
            }
        }

        previousText = previousText.TrimEnd();
        if (previousText.Length == 0)
        {
            return false;
        }

        var tail = previousText[^1];
        return tail is '.' or '!' or '?' or ';' or ':' or '”' or '»';
    }

    private static bool IsBoundaryPunctuation(char value)
    {
        return value is '.' or '۔' or '!' or '?' or '؟' or ':' or ';' or ')' or ']' or '"' or '\'' or '»' or '”';
    }

    private static bool TryParseLeadingVerse(string text, out int verseNumber, out string verseText)
    {
        verseNumber = 0;
        verseText = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var i = 0;
        while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] is '"' or '\'' or '«' or '“' or '[' or '('))
        {
            i++;
        }

        if (i >= text.Length || !char.IsDigit(text[i]))
        {
            return false;
        }

        var start = i;
        while (i < text.Length && char.IsDigit(text[i]) && i - start < 3)
        {
            i++;
        }

        if (!TryParseVerseOrChapterNumber(text[start..i], out verseNumber) || verseNumber <= 0)
        {
            return false;
        }

        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i < text.Length && text[i] is '.' or ')')
        {
            i++;
        }

        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i >= text.Length)
        {
            return false;
        }

        verseText = text[i..].Trim();
        return verseText.Length > 0;
    }

    private static string BuildParagraphTextWithFootnotes(
        XElement paragraph,
        IReadOnlyDictionary<string, string> footnotesById,
        ContractMode mode,
        ICollection<ContractIssue> issues)
    {
        var sb = new StringBuilder();

        foreach (var node in paragraph.Descendants())
        {
            if (node.Name == W + "t")
            {
                sb.Append(node.Value);
                continue;
            }

            if (node.Name == W + "footnoteReference")
            {
                var id = (string?)node.Attribute(W + "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (footnotesById.TryGetValue(id, out var noteText) && !string.IsNullOrWhiteSpace(noteText))
                {
                    sb.Append(" \\f + \\ft ");
                    sb.Append(NormalizeWhitespace(noteText).Trim());
                    sb.Append(" \\f*");
                }
                else
                {
                    issues.Add(Issue(
                        mode,
                        "FOOTNOTE_MISSING_TEXT",
                        $"Footnote reference id '{id}' has no corresponding footnote text."));
                }
            }
        }

        return sb.ToString();
    }

    private static IEnumerable<ParagraphToken> FlattenParagraphTokens(XElement paragraph)
    {
        foreach (var node in paragraph.Descendants())
        {
            if (node.Name == W + "r")
            {
                var styleId = node
                    .Element(W + "rPr")?
                    .Element(W + "rStyle")?
                    .Attribute(W + "val")?
                    .Value;

                var textValue = ExtractRunText(node);
                if (string.IsNullOrEmpty(textValue))
                {
                    continue;
                }

                if (string.Equals(styleId, UsfmDocxContractV1.CharacterStyles.VerseNumber, StringComparison.Ordinal))
                {
                    yield return new ParagraphToken(ParagraphTokenKind.VerseNumber, textValue);
                }
                else
                {
                    yield return new ParagraphToken(ParagraphTokenKind.Text, textValue);
                }

                continue;
            }

            if (node.Name == W + "footnoteReference")
            {
                var id = (string?)node.Attribute(W + "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    yield return new ParagraphToken(ParagraphTokenKind.FootnoteReference, id);
                }
            }
        }
    }

    private static string ExtractRunText(XElement run)
    {
        var sb = new StringBuilder();

        foreach (var child in run.Elements())
        {
            if (child.Name == W + "t")
            {
                sb.Append(child.Value);
            }
            else if (child.Name == W + "tab")
            {
                sb.Append('\t');
            }
            else if (child.Name == W + "br")
            {
                sb.Append(' ');
            }
        }

        return sb.ToString();
    }

    private static string ExtractNodeText(XElement node)
    {
        var sb = new StringBuilder();

        foreach (var textNode in node.Descendants(W + "t"))
        {
            sb.Append(textNode.Value);
        }

        return sb.ToString();
    }

    private static string CleanVerseBodyLead(string value)
    {
        return Regex.Replace(value.Trim(), @"^[.)۔:]+\s*", string.Empty);
    }

    private static string NormalizeWhitespace(string value)
    {
        var normalized = StripDirectionalFormatting(value.Replace('\u00A0', ' '));
        if (ContainsArabicScript(normalized))
        {
            return ScripturePunctuationNormalizer.NormalizeArabicDerivedSpacing(normalized);
        }

        return Regex.Replace(normalized, "\\s+", " ");
    }

    private static string NormalizeChapterDisplayLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var normalized = NormalizeWhitespace(value).Trim();
        if (!ContainsArabicScript(normalized))
        {
            return normalized;
        }

        return ToArabicIndicDigits(normalized);
    }

    private static string NormalizeHeadingText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return StripArabicDiacritics(NormalizeWhitespace(value))
            .Trim()
            .ToLowerInvariant();
    }

    private static string StripDirectionalFormatting(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value
            .Replace("\u200E", string.Empty)
            .Replace("\u200F", string.Empty)
            .Replace("\u202A", string.Empty)
            .Replace("\u202B", string.Empty)
            .Replace("\u202C", string.Empty)
            .Replace("\u202D", string.Empty)
            .Replace("\u202E", string.Empty)
            .Replace("\u2066", string.Empty)
            .Replace("\u2067", string.Empty)
            .Replace("\u2068", string.Empty)
            .Replace("\u2069", string.Empty);
    }

    private static bool ContainsArabicScript(string value)
    {
        foreach (var ch in value)
        {
            if (ch >= '\u0600' && ch <= '\u06FF')
            {
                return true;
            }
        }

        return false;
    }

    private static string StripArabicDiacritics(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if ((ch >= '\u064B' && ch <= '\u065F') || ch == '\u0670')
            {
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static bool TryParseVerseOrChapterNumber(string raw, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = NormalizeIndicDigits(raw.Trim());
        return int.TryParse(normalized, out number) && number > 0;
    }

    private static string NormalizeIndicDigits(string value)
    {
        var buffer = value.ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            var ch = buffer[i];
            if (ch >= '\u0660' && ch <= '\u0669')
            {
                buffer[i] = (char)('0' + (ch - '\u0660'));
            }
            else if (ch >= '\u06F0' && ch <= '\u06F9')
            {
                buffer[i] = (char)('0' + (ch - '\u06F0'));
            }
        }

        return new string(buffer);
    }

    private static string ToArabicIndicDigits(string value)
    {
        var buffer = value.ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] >= '0' && buffer[i] <= '9')
            {
                buffer[i] = (char)('\u06F0' + (buffer[i] - '0'));
            }
        }

        return new string(buffer);
    }

    private static bool TryParseUsfmVerseMarkerOnly(string text, out int verseNumber)
    {
        verseNumber = 0;
        var match = UsfmVerseMarkerOnlyRegex.Match(text);
        if (match.Success)
        {
            return TryParseVerseOrChapterNumber(match.Groups[1].Value, out verseNumber);
        }

        match = VSlashVerseMarkerOnlyRegex.Match(text);
        return match.Success && TryParseVerseOrChapterNumber(match.Groups[1].Value, out verseNumber);
    }

    private static bool TryParseUsfmVerseInline(string text, out int verseNumber, out string verseText)
    {
        verseNumber = 0;
        verseText = string.Empty;
        var match = UsfmVerseInlineRegex.Match(text);
        if (!match.Success)
        {
            match = VSlashVerseInlineRegex.Match(text);
        }

        if (!match.Success || !TryParseVerseOrChapterNumber(match.Groups[1].Value, out verseNumber))
        {
            return false;
        }

        verseText = match.Groups[2].Value.Trim();
        return verseText.Length > 0;
    }

    private static bool TryParsePlainVerseMarkerOnly(string text, out int verseNumber)
    {
        verseNumber = 0;
        var match = PlainVerseMarkerOnlyRegex.Match(text);
        if (!match.Success)
        {
            return false;
        }

        return TryParseVerseOrChapterNumber(match.Groups[1].Value, out verseNumber);
    }

    private static void ApplyKnownRussianVersificationFixes(
        IList<string> lines,
        ContractMode mode,
        ICollection<ContractIssue> issues,
        string canonToken,
        IReadOnlySet<string>? selectedBookIds)
    {
        // If conversion explicitly targets books other than NUM, do not apply Numbers-specific verse remap.
        if (selectedBookIds is { Count: > 0 }
            && !selectedBookIds.Contains("NUM"))
        {
            return;
        }

        // Preserve Orthodox OT source versification (e.g., LXX-aligned handling).
        if (canonToken.StartsWith("orthodox", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue(
                mode,
                "VERSIFICATION_PROFILE_PRESERVED",
                "Orthodox canon selected: skipped Protestant-specific Numbers remap."));
            return;
        }

        // Russian source-specific correction:
        // Numbers 13:1 "После (того) народ..." should be numbered as Numbers 12:16
        // and subsequent Numbers 13 verses should shift down by 1.
        var numbersMtIndex = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("\\mt ", StringComparison.Ordinal)
                && lines[i].Contains("Числа", StringComparison.OrdinalIgnoreCase))
            {
                numbersMtIndex = i;
                break;
            }
        }

        if (numbersMtIndex < 0)
        {
            return;
        }

        var c12Index = -1;
        var c13Index = -1;
        var c14Index = -1;

        for (var i = numbersMtIndex; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("\\mt ", StringComparison.Ordinal) && i > numbersMtIndex)
            {
                break;
            }

            if (lines[i] == "\\c 12")
            {
                c12Index = i;
            }
            else if (lines[i] == "\\c 13")
            {
                c13Index = i;
            }
            else if (lines[i] == "\\c 14")
            {
                c14Index = i;
                break;
            }
        }

        if (c12Index < 0 || c13Index < 0)
        {
            return;
        }

        var chapter13End = c14Index > 0 ? c14Index : lines.Count;

        var has1216 = false;
        for (var i = c12Index; i < c13Index; i++)
        {
            if (lines[i].StartsWith("\\v 16 ", StringComparison.Ordinal))
            {
                has1216 = true;
                break;
            }
        }

        if (has1216)
        {
            return;
        }

        var chapter13Verse1Index = -1;
        string? chapter13Verse1Text = null;
        for (var i = c13Index; i < chapter13End; i++)
        {
            if (lines[i].StartsWith("\\v 1 ", StringComparison.Ordinal))
            {
                chapter13Verse1Index = i;
                chapter13Verse1Text = lines[i][5..];
                break;
            }
        }

        if (chapter13Verse1Index < 0 || string.IsNullOrWhiteSpace(chapter13Verse1Text))
        {
            return;
        }

        if (!chapter13Verse1Text.StartsWith("После", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lines.Insert(c13Index, $"\\v 16 {chapter13Verse1Text}");
        var chapter13MarkerIndex = c13Index + 1;
        chapter13Verse1Index++;
        chapter13End++;

        lines.RemoveAt(chapter13Verse1Index);
        chapter13End--;

        for (var i = chapter13MarkerIndex + 1; i < chapter13End; i++)
        {
            if (!lines[i].StartsWith("\\v ", StringComparison.Ordinal))
            {
                continue;
            }

            var match = Regex.Match(lines[i], "^\\\\v\\s+(\\d+)\\s+(.*)$");
            if (!match.Success)
            {
                continue;
            }

            if (!int.TryParse(match.Groups[1].Value, out var verse))
            {
                continue;
            }

            if (verse >= 2)
            {
                lines[i] = $"\\v {verse - 1} {match.Groups[2].Value}";
            }
        }

        issues.Add(Issue(
            mode,
            "VERSIFICATION_REMAP",
            "Applied Numbers versification remap: source 13:1 moved to 12:16, and chapter 13 verses shifted."));
    }

    private static void ValidateChapterVerseContinuity(
        IReadOnlyList<string> lines,
        ContractMode mode,
        ICollection<ContractIssue> issues)
    {
        string? currentBook = null;
        var currentChapter = 0;
        var seenVerseInChapter = false;
        var previousVerse = 0;
        var seenVerses = new HashSet<int>();

        static string Ref(string? book, int chapter) =>
            string.IsNullOrWhiteSpace(book) ? $"chapter {chapter}" : $"{book} {chapter}";

        foreach (var line in lines)
        {
            if (line.StartsWith("\\mt ", StringComparison.Ordinal))
            {
                currentBook = line[4..].Trim();
                currentChapter = 0;
                seenVerseInChapter = false;
                previousVerse = 0;
                seenVerses.Clear();
                continue;
            }

            if (line.StartsWith("\\c ", StringComparison.Ordinal))
            {
                if (currentChapter > 0 && !seenVerseInChapter)
                {
                    issues.Add(Issue(
                        mode,
                        "CHAPTER_WITHOUT_VERSES",
                        $"{Ref(currentBook, currentChapter)} has no parsed verses."));
                }

                var chapterRaw = line[3..].Trim();
                if (!int.TryParse(chapterRaw, out var parsedChapter) || parsedChapter <= 0)
                {
                    issues.Add(Issue(mode, "CHAPTER_INVALID_NUMBER", $"Invalid chapter marker: '{line}'"));
                    continue;
                }

                if (currentChapter > 0 && parsedChapter != currentChapter + 1)
                {
                    issues.Add(Issue(
                        mode,
                        "CHAPTER_SEQUENCE_GAP",
                        $"Chapter sequence jump in {currentBook}: {currentChapter} -> {parsedChapter}."));
                }

                currentChapter = parsedChapter;
                seenVerseInChapter = false;
                previousVerse = 0;
                seenVerses.Clear();
                continue;
            }

            if (!line.StartsWith("\\v ", StringComparison.Ordinal) || currentChapter <= 0)
            {
                continue;
            }

            var match = Regex.Match(line, "^\\\\v\\s+(\\d+)\\b");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var verse) || verse <= 0)
            {
                issues.Add(Issue(mode, "VERSE_INVALID_NUMBER", $"Invalid verse marker: '{line}'"));
                continue;
            }

            seenVerseInChapter = true;

            if (verse == 1 && previousVerse > 0)
            {
                issues.Add(Issue(
                    mode,
                    "VERSE_RESTART_WITHOUT_CHAPTER",
                    $"Verse restarted at 1 without new chapter near {Ref(currentBook, currentChapter)}."));
            }

            if (previousVerse > 0 && verse > previousVerse + 1)
            {
                issues.Add(Issue(
                    mode,
                    "VERSE_SEQUENCE_GAP",
                    $"Missing verse(s) in {Ref(currentBook, currentChapter)}: {previousVerse} -> {verse}."));
            }

            if (seenVerses.Contains(verse))
            {
                issues.Add(Issue(
                    mode,
                    "VERSE_DUPLICATE",
                    $"Duplicate verse number {verse} in {Ref(currentBook, currentChapter)}."));
            }

            if (previousVerse > 0 && verse < previousVerse && verse != 1)
            {
                issues.Add(Issue(
                    mode,
                    "VERSE_OUT_OF_ORDER",
                    $"Verse order decreased in {Ref(currentBook, currentChapter)}: {previousVerse} -> {verse}."));
            }

            seenVerses.Add(verse);
            previousVerse = verse;
        }

        if (currentChapter > 0 && !seenVerseInChapter)
        {
            issues.Add(Issue(
                mode,
                "CHAPTER_WITHOUT_VERSES",
                $"{Ref(currentBook, currentChapter)} has no parsed verses."));
        }
    }

    private static ContractIssue Issue(ContractMode mode, string code, string message)
    {
        return mode == ContractMode.Strict
            ? new ContractIssue(Severity.Error, code, message)
            : new ContractIssue(Severity.Warning, code, message);
    }

    private readonly record struct ParagraphToken(ParagraphTokenKind Kind, string Value);

    private enum ParagraphTokenKind
    {
        VerseNumber,
        Text,
        FootnoteReference
    }

    private readonly record struct BodyConversionResult(
        int VerseCount,
        IReadOnlyDictionary<string, int> UnknownParagraphStyleCounts);

    private readonly record struct VerseSegment(int Number, string Text);

    internal sealed record ConversionResult(
        IReadOnlyList<string> Lines,
        IReadOnlyList<ContractIssue> Issues);
}
