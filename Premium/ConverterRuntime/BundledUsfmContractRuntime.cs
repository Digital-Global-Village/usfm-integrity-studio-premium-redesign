using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UsfmConverter.Contracts;


namespace UsfmIntegrityStudio.ConverterRuntime;

internal sealed record BundledConversionExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal static class BundledUsfmContractRuntime
{
    private static readonly object ExecutionGate = new();
    private static TextWriter StandardOutput { get; set; } = TextWriter.Null;
    private static TextWriter StandardError { get; set; } = TextWriter.Null;

    public static BundledConversionExecutionResult Execute(string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        lock (ExecutionGate)
        {
            StandardOutput = stdout;
            StandardError = stderr;
            try
            {
                var exitCode = Dispatch(args);
                return new BundledConversionExecutionResult(exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                StandardOutput = TextWriter.Null;
                StandardError = TextWriter.Null;
            }
        }
    }

    private static int Dispatch(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        ProfileContext.Current = LanguageProfileLoader.LoadDefault();
        return args[0].ToLowerInvariant() switch
        {
            "docx-to-usfm" => RunDocxToUsfm(args),
            _ => WriteUnknownCommand(args[0])
        };
    }

    private static int WriteUnknownCommand(string command)
    {
        StandardError.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }
    static int RunValidateMarker(string[] args)
    {
        if (args.Length < 2)
        {
            StandardError.WriteLine("Missing marker argument.");
            PrintUsage();
            return 1;
        }

        var marker = args[1];
        var mode = ParseMode(args.Length > 2 ? args[2] : null);

        var issues = UsfmDocxContractV1.ValidateMarker(marker, mode);
        return PrintIssues(issues);
    }

    static int RunValidateStyles(string[] args)
    {
        if (args.Length < 3)
        {
            StandardError.WriteLine("Missing style arguments.");
            PrintUsage();
            return 1;
        }

        var paragraphStyles = ParseCsvToSet(args[1]);
        var characterStyles = ParseCsvToSet(args[2]);
        var mode = ParseMode(args.Length > 3 ? args[3] : null);

        var issues = UsfmDocxContractV1.ValidateStyles(paragraphStyles, characterStyles, mode);
        return PrintIssues(issues);
    }

    static int RunDocxToUsfm(string[] args)
    {
        if (args.Length < 3)
        {
            StandardError.WriteLine("Missing DOCX conversion arguments.");
            PrintUsage();
            return 1;
        }

        var inputDocxPath = args[1];
        var outputTarget = args[2];

        var mode = ContractMode.Permissive;
        string? reportPath = null;
        var splitBooks = false;
        string? onlyBookId = null;
        HashSet<string>? onlyBookIds = null;
        string? profileId = null;
        var canonToken = "protestant-ot";
        var preserveVerseMarkers = false;
        string? verseTxtPath = null;
        string? langCode = null;
        var resourceId = "reg";
        string? producerTag = null;

        var index = 3;
        if (index < args.Length && IsModeToken(args[index]))
        {
            mode = ParseMode(args[index]);
            index++;
        }

        while (index < args.Length)
        {
            var token = args[index];

            if (string.Equals(token, "--split-books", StringComparison.OrdinalIgnoreCase))
            {
                splitBooks = true;
                index++;
                continue;
            }

            if (string.Equals(token, "--report", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    StandardError.WriteLine("Missing value for --report.");
                    return 1;
                }

                reportPath = args[index + 1];
                index += 2;
                continue;
            }

            if (string.Equals(token, "--book", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    StandardError.WriteLine("Missing value for --book.");
                    return 1;
                }

                onlyBookId = args[index + 1].Trim().ToUpperInvariant();
                index += 2;
                continue;
            }

            if (string.Equals(token, "--books", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    StandardError.WriteLine("Missing value for --books.");
                    return 1;
                }

                onlyBookIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in args[index + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    onlyBookIds.Add(raw.Trim().ToUpperInvariant());
                }

                index += 2;
                continue;
            }

            if (string.Equals(token, "--profile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "--language-profile", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    StandardError.WriteLine("Missing value for --profile.");
                    return 1;
                }

                profileId = args[index + 1].Trim();
                index += 2;
                continue;
            }

            if (string.Equals(token, "--canon", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    StandardError.WriteLine("Missing value for --canon.");
                    return 1;
                }

                canonToken = args[index + 1].Trim().ToLowerInvariant();
                index += 2;
                continue;
            }

            if (string.Equals(token, "--preserve-verse-markers", StringComparison.OrdinalIgnoreCase))
            {
                preserveVerseMarkers = true;
                index++;
                continue;
            }

            if (string.Equals(token, "--lang-code", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    StandardError.WriteLine("Missing value for --lang-code.");
                    return 1;
                }

                langCode = args[index + 1].Trim().ToLowerInvariant();
                index += 2;
                continue;
            }

            if (string.Equals(token, "--resource-id", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    StandardError.WriteLine("Missing value for --resource-id.");
                    return 1;
                }

                resourceId = args[index + 1].Trim().ToLowerInvariant();
                index += 2;
                continue;
            }

            if (string.Equals(token, "--producer-tag", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    StandardError.WriteLine("Missing value for --producer-tag.");
                    return 1;
                }

                producerTag = args[index + 1].Trim().ToLowerInvariant();
                index += 2;
                continue;
            }

            if (string.Equals(token, "--verse-txt", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    StandardError.WriteLine("Missing value for --verse-txt.");
                    return 1;
                }

                verseTxtPath = args[index + 1];
                index += 2;
                continue;
            }

            StandardError.WriteLine($"Unknown option: {token}");
            PrintUsage();
            return 1;
        }

        if (!LanguageProfileLoader.TryResolve(profileId, out var resolvedProfile, out var resolveError))
        {
            StandardError.WriteLine(resolveError);
            return 1;
        }

        ProfileContext.Current = resolvedProfile;

        if (!string.IsNullOrWhiteSpace(onlyBookId))
        {
            onlyBookIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            onlyBookIds.Add(onlyBookId);
        }

        var result = DocxToUsfmConverter.Convert(inputDocxPath, mode, canonToken, onlyBookIds, preserveVerseMarkers);

        if (result.Lines.Count > 0)
        {
            if (splitBooks)
            {
                var files = WriteSplitBooks(outputTarget, result.Lines, onlyBookIds, preserveVerseMarkers, langCode, resourceId, producerTag);
                if (files.Count == 0)
                {
                    var selected = onlyBookIds is null ? string.Empty : string.Join(",", onlyBookIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                    StandardError.WriteLine($"No matching book found for selected set: {selected}.");
                    return 1;
                }
                StandardOutput.WriteLine($"Generated {files.Count} USFM file(s) in: {Path.GetDirectoryName(files[0])}");
                foreach (var file in files)
                {
                    StandardOutput.WriteLine($"Generated USFM file: {Path.GetFullPath(file)}");
                }
            }
            else
            {
                var linesToWrite = result.Lines;
                if (onlyBookIds is { Count: > 0 })
                {
                    if (onlyBookIds.Count > 1)
                    {
                        StandardError.WriteLine("Multiple book selection requires --split-books.");
                        return 1;
                    }

                    var oneBook = onlyBookIds.First();
                    if (!TrySelectBookLines(result.Lines, oneBook, preserveVerseMarkers, out var selectedLines))
                    {
                        StandardError.WriteLine($"No matching book found for --book {oneBook}.");
                        return 1;
                    }

                    linesToWrite = selectedLines;
                }

                WriteUsfmFile(outputTarget, linesToWrite);
                StandardOutput.WriteLine($"Generated USFM file: {Path.GetFullPath(outputTarget)}");
            }

            if (!string.IsNullOrWhiteSpace(verseTxtPath))
            {
                var verseLines = SelectLinesForVerseExport(result.Lines, onlyBookIds, preserveVerseMarkers);
                if (verseLines.Count == 0)
                {
                    StandardError.WriteLine("No matching verse content found for --verse-txt export.");
                    return 1;
                }

                WriteVersePerLineTextFile(verseTxtPath, verseLines);
                StandardOutput.WriteLine($"Generated verse TXT file: {Path.GetFullPath(verseTxtPath)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var fullReportPath = Path.GetFullPath(reportPath);
            WriteIssueReport(fullReportPath, inputDocxPath, splitBooks, outputTarget, result.Issues);
            StandardOutput.WriteLine($"Wrote report: {fullReportPath}");
        }

        var maxConsoleIssues = string.IsNullOrWhiteSpace(reportPath) ? int.MaxValue : 80;
        return PrintIssues(result.Issues, maxConsoleIssues, reportPath);
    }

    static bool IsModeToken(string token)
    {
        return string.Equals(token, "strict", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "permissive", StringComparison.OrdinalIgnoreCase);
    }

    static ContractMode ParseMode(string? raw)
    {
        if (string.Equals(raw, "strict", StringComparison.OrdinalIgnoreCase))
        {
            return ContractMode.Strict;
        }

        return ContractMode.Permissive;
    }

    static HashSet<string> ParseCsvToSet(string csv)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var tokens = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            set.Add(token);
        }

        return set;
    }

    static int PrintIssues(IEnumerable<ContractIssue> issues, int maxConsoleIssues = int.MaxValue, string? reportPath = null)
    {
        var issueList = issues.ToList();

        var printed = 0;
        foreach (var issue in issueList)
        {
            if (printed >= maxConsoleIssues)
            {
                break;
            }

            printed++;
            StandardOutput.WriteLine($"[{issue.Severity}] {issue.Code}: {issue.Message}");
        }

        if (issueList.Count == 0)
        {
            StandardOutput.WriteLine("OK: no issues found.");
            return 0;
        }

        if (printed < issueList.Count)
        {
            StandardOutput.WriteLine($"... {issueList.Count - printed} additional issue(s) omitted from console output.");
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                StandardOutput.WriteLine($"See full report: {Path.GetFullPath(reportPath)}");
            }
        }

        return 2;
    }

    static void WriteUsfmFile(string outputPath, IReadOnlyList<string> lines)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var output = string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
        File.WriteAllText(fullPath, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    static void WriteVersePerLineTextFile(string outputPath, IReadOnlyList<string> lines)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var output = BuildVersePerLineText(lines);
        File.WriteAllText(fullPath, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    static string BuildVersePerLineText(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        var book = "BOOK";
        var chapter = 0;
        var bookHeaderWritten = false;
        var verseRegex = new Regex(@"^\\v\s+(\d+)\s*(.*)$", RegexOptions.CultureInvariant);

        foreach (var line in lines)
        {
            if (line.StartsWith("\\id ", StringComparison.Ordinal))
            {
                var idParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (idParts.Length >= 2)
                {
                    book = idParts[1].Trim().ToUpperInvariant();
                    bookHeaderWritten = false;
                }
                continue;
            }

            if (line.StartsWith("\\c ", StringComparison.Ordinal))
            {
                var raw = line[3..].Trim();
                if (int.TryParse(raw, out var parsedChapter) && parsedChapter > 0)
                {
                    chapter = parsedChapter;
                }
                continue;
            }

            if (!line.StartsWith("\\v ", StringComparison.Ordinal))
            {
                continue;
            }

            var match = verseRegex.Match(line);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var verse))
            {
                continue;
            }

            if (!bookHeaderWritten)
            {
                sb.AppendLine($"# {book}");
                bookHeaderWritten = true;
            }

            var text = match.Groups[2].Value.Trim();
            sb.Append(chapter);
            sb.Append(':');
            sb.Append(verse);
            sb.Append('\t');
            sb.AppendLine(text);
        }

        return sb.ToString();
    }

    static IReadOnlyList<string> SelectLinesForVerseExport(
        IReadOnlyList<string> lines,
        IReadOnlySet<string>? onlyBookIds,
        bool preserveVerseMarkers)
    {
        if (onlyBookIds is not { Count: > 0 })
        {
            return lines;
        }

        var segments = SplitIntoBookSegments(lines)
            .Where(segment =>
            {
                var id = InferBookId(segment.Title);
                return !string.IsNullOrWhiteSpace(id) && onlyBookIds.Contains(id);
            })
            .ToList();

        if (segments.Count == 0)
        {
            return Array.Empty<string>();
        }

        var selected = new List<string>();
        foreach (var segment in segments)
        {
            var id = InferBookId(segment.Title);
            var trimmed = string.IsNullOrWhiteSpace(id)
                ? segment.Lines.ToList()
                : TrimToKnownBookBoundaries(segment.Lines, id).ToList();
            var exportLines = EnsureSplitBookHeaders(new BookSegment(segment.Title, trimmed), preserveVerseMarkers);

            if (selected.Count > 0)
            {
                selected.Add(string.Empty);
            }

            selected.AddRange(exportLines);
        }

        return selected;
    }

    static List<string> WriteSplitBooks(
        string outputTarget,
        IReadOnlyList<string> lines,
        IReadOnlySet<string>? onlyBookIds = null,
        bool preserveVerseMarkers = false,
        string? langCode = null,
        string resourceId = "reg",
        string? producerTag = null)
    {
        if (onlyBookIds is { Count: 1 })
        {
            var singleBookId = onlyBookIds.First().Trim().ToUpperInvariant();
            var trimmed = TrimToKnownBookBoundaries(lines, singleBookId).ToList();
            if (trimmed.Count > 0)
            {
                var singleOutputDirectory = ResolveSplitOutputDirectory(outputTarget);
                Directory.CreateDirectory(singleOutputDirectory);

                var segment = new BookSegment(singleBookId, trimmed);
                var linesForExport = EnsureSplitBookHeadersWithFallbackId(segment, preserveVerseMarkers, singleBookId, resourceId);
                var fileName = BuildUsfmFileName(singleBookId, langCode, resourceId, producerTag, 1);
                var path = Path.Combine(singleOutputDirectory, fileName);
                WriteUsfmFile(path, linesForExport);
                return [path];
            }
        }

        var allSegments = SplitIntoBookSegments(lines);
        var segments = allSegments;
        string? forcedBookId = null;
        if (onlyBookIds is { Count: > 0 })
        {
            segments = segments
                .Where(segment =>
                {
                    var id = InferBookId(segment.Title);
                    return !string.IsNullOrWhiteSpace(id) && onlyBookIds.Contains(id);
                })
                .Select(segment =>
                {
                    var id = InferBookId(segment.Title);
                    var trimmed = string.IsNullOrWhiteSpace(id)
                        ? segment.Lines.ToList()
                        : TrimToKnownBookBoundaries(segment.Lines, id).ToList();
                    return new BookSegment(segment.Title, trimmed);
                })
                .ToList();

            if (segments.Count == 0 && allSegments.Count == 1 && onlyBookIds.Count == 1)
            {
                // Fallback: single-book DOCX with heading text that did not map cleanly.
                // Honor user-selected book id instead of failing hard.
                segments = allSegments;
                forcedBookId = onlyBookIds.First().Trim().ToUpperInvariant();
            }

            if (segments.Count == 0 && onlyBookIds.Count == 1)
            {
                // Broader fallback for merged chapter-folder workflows:
                // if the UI already constrained the run to one selected book,
                // export the trimmed full content under that forced book id.
                forcedBookId = onlyBookIds.First().Trim().ToUpperInvariant();
                var trimmed = TrimToKnownBookBoundaries(lines, forcedBookId).ToList();
                if (trimmed.Count > 0)
                {
                    segments = new List<BookSegment>
                {
                    new BookSegment(forcedBookId, trimmed)
                };
                }
            }
        }

        var outputDirectory = ResolveSplitOutputDirectory(outputTarget);
        Directory.CreateDirectory(outputDirectory);

        var files = new List<string>();
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var bookId = InferBookId(segment.Title) ?? forcedBookId ?? segment.Title;
            var fileName = BuildUsfmFileName(bookId, langCode, resourceId, producerTag, i + 1);
            var path = Path.Combine(outputDirectory, fileName);
            var linesForExport = EnsureSplitBookHeadersWithFallbackId(segment, preserveVerseMarkers, forcedBookId, resourceId);
            WriteUsfmFile(path, linesForExport);
            files.Add(path);
        }

        return files;
    }

    static bool TrySelectBookLines(IReadOnlyList<string> lines, string bookId, bool preserveVerseMarkers, out IReadOnlyList<string> selected)
    {
        var segments = SplitIntoBookSegments(lines);
        var matches = segments.Where(segment =>
            string.Equals(InferBookId(segment.Title), bookId, StringComparison.OrdinalIgnoreCase));
        var matchingSegments = matches.ToList();

        if (matchingSegments.Count == 0)
        {
            var trimmedFallback = TrimToKnownBookBoundaries(lines, bookId).ToList();
            if (trimmedFallback.Count == 0)
            {
                selected = Array.Empty<string>();
                return false;
            }

            selected = EnsureSplitBookHeaders(new BookSegment(bookId, trimmedFallback), preserveVerseMarkers);
            return true;
        }

        var mergedLines = matchingSegments.Count == 1
            ? matchingSegments[0].Lines.ToList()
            : matchingSegments.SelectMany(segment => segment.Lines).ToList();

        var mergedTitle = matchingSegments[0].Title;
        var trimmed = new BookSegment(mergedTitle, TrimToKnownBookBoundaries(mergedLines, bookId).ToList());
        selected = EnsureSplitBookHeaders(trimmed, preserveVerseMarkers);
        return true;
    }

    static IReadOnlyList<string> TrimToKnownBookBoundaries(IReadOnlyList<string> lines, string bookId)
    {
        var maxChapterByBook = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["GEN"] = 50,
            ["EXO"] = 40,
            ["LEV"] = 27,
            ["NUM"] = 36,
            ["DEU"] = 34,
            ["JOS"] = 24,
            ["JDG"] = 21,
            ["RUT"] = 4,
            ["1SA"] = 31,
            ["2SA"] = 24,
            ["1KI"] = 22,
            ["2KI"] = 25,
            ["1CH"] = 29,
            ["2CH"] = 36,
            ["EZR"] = 10,
            ["NEH"] = 13,
            ["EST"] = 10,
            ["JOB"] = 42,
            ["PSA"] = 150,
            ["PRO"] = 31,
            ["ECC"] = 12,
            ["SNG"] = 8,
            ["ISA"] = 66,
            ["JER"] = 52,
            ["LAM"] = 5,
            ["EZK"] = 48,
            ["DAN"] = 12,
            ["HOS"] = 14,
            ["JOL"] = 3,
            ["AMO"] = 9,
            ["OBA"] = 1,
            ["JON"] = 4,
            ["MIC"] = 7,
            ["NAM"] = 3,
            ["HAB"] = 3,
            ["ZEP"] = 3,
            ["HAG"] = 2,
            ["ZEC"] = 14,
            ["MAL"] = 4,
            ["MAT"] = 28,
            ["MRK"] = 16,
            ["LUK"] = 24,
            ["JHN"] = 21,
            ["ACT"] = 28,
            ["ROM"] = 16,
            ["1CO"] = 16,
            ["2CO"] = 13,
            ["GAL"] = 6,
            ["EPH"] = 6,
            ["PHP"] = 4,
            ["COL"] = 4,
            ["1TH"] = 5,
            ["2TH"] = 3,
            ["1TI"] = 6,
            ["2TI"] = 4,
            ["TIT"] = 3,
            ["PHM"] = 1,
            ["HEB"] = 13,
            ["JAS"] = 5,
            ["1PE"] = 5,
            ["2PE"] = 3,
            ["1JN"] = 5,
            ["2JN"] = 1,
            ["3JN"] = 1,
            ["JUD"] = 1,
            ["REV"] = 22
        };

        if (!maxChapterByBook.TryGetValue(bookId, out var maxChapter) || maxChapter <= 0)
        {
            return TrimToGenericBookBoundaries(lines);
        }

        var output = new List<string>(lines.Count);
        var startedChapters = false;
        var currentChapter = 0;
        var seenMaxChapter = false;

        foreach (var line in lines)
        {
            if (seenMaxChapter)
            {
                if (line.StartsWith("\\mt ", StringComparison.Ordinal)
                    || line.StartsWith("\\id ", StringComparison.Ordinal)
                    || line.StartsWith("\\h ", StringComparison.Ordinal)
                    || line.StartsWith("\\toc", StringComparison.Ordinal))
                {
                    break;
                }

                if (IsLikelyStandaloneBookHeading(line))
                {
                    break;
                }

                if (TryTrimInlineNextBookHeading(line, out var trimmedLine))
                {
                    if (!string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        output.Add(trimmedLine);
                    }
                    break;
                }
            }

            if (!line.StartsWith("\\c ", StringComparison.Ordinal))
            {
                if (!startedChapters || currentChapter > 0)
                {
                    output.Add(line);
                }
                continue;
            }

            var raw = line[3..].Trim();
            if (!int.TryParse(raw, out var chapter) || chapter <= 0)
            {
                if (startedChapters)
                {
                    break;
                }
                output.Add(line);
                continue;
            }

            if (!startedChapters)
            {
                startedChapters = true;
                currentChapter = chapter;
                seenMaxChapter = chapter >= maxChapter;
                output.Add(line);
                continue;
            }

            if (seenMaxChapter && chapter == 1)
            {
                break;
            }

            if (chapter > maxChapter)
            {
                break;
            }

            currentChapter = chapter;
            if (chapter == maxChapter)
            {
                seenMaxChapter = true;
            }

            output.Add(line);
        }

        return output;
    }

    static IReadOnlyList<string> TrimToGenericBookBoundaries(IReadOnlyList<string> lines)
    {
        var output = new List<string>(lines.Count);
        var startedChapters = false;
        var currentChapter = 0;

        foreach (var line in lines)
        {
            if (!line.StartsWith("\\c ", StringComparison.Ordinal))
            {
                if (startedChapters && IsLikelyStandaloneBookHeading(line))
                {
                    break;
                }

                if (startedChapters && TryTrimInlineNextBookHeading(line, out var trimmedLine))
                {
                    if (!string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        output.Add(trimmedLine);
                    }

                    break;
                }

                output.Add(line);
                continue;
            }

            var raw = line[3..].Trim();
            if (!int.TryParse(raw, out var chapter) || chapter <= 0)
            {
                output.Add(line);
                continue;
            }

            if (!startedChapters)
            {
                startedChapters = true;
                currentChapter = chapter;
                output.Add(line);
                continue;
            }

            // Generic end-of-book heuristic when a new heading was missed:
            // chapter numbers restart from 1 after progressing beyond chapter 1.
            if (chapter == 1 && currentChapter > 1)
            {
                break;
            }

            if (chapter < currentChapter && chapter <= 3 && currentChapter > 3)
            {
                break;
            }

            currentChapter = chapter;
            output.Add(line);
        }

        return output;
    }

    static bool TryTrimInlineNextBookHeading(string line, out string trimmed)
    {
        trimmed = line;
        if (!line.StartsWith("\\v ", StringComparison.Ordinal))
        {
            return false;
        }

        var index = FindInlineBookHeadingStartIndex(line);
        if (index <= 0)
        {
            return false;
        }

        var suffix = line[(index + 1)..].Trim();
        if (!IsLikelyBookHeadingText(suffix))
        {
            return false;
        }

        trimmed = line[..index].TrimEnd();
        return true;
    }

    static bool IsLikelyStandaloneBookHeading(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("\\", StringComparison.Ordinal))
        {
            return false;
        }

        return IsLikelyBookHeadingText(line.Trim());
    }

    static bool IsLikelyBookHeadingText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.Length > 140)
        {
            return false;
        }

        if (Regex.IsMatch(value, "[.!?]\\s+"))
        {
            return false;
        }

        var hasKnownPrefix = ProfileContext.Current.HeadingPrefixes.Any(prefix =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return hasKnownPrefix || HasKnownBookName(value);
    }

    static bool HasKnownBookName(string heading)
    {
        if (string.IsNullOrWhiteSpace(heading))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(InferBookId(heading));
    }

    static IReadOnlyList<string> EnsureSplitBookHeaders(BookSegment segment, bool preserveVerseMarkers)
    {
        var bookId = InferBookId(segment.Title);
        var segmentLines = string.Equals(bookId, "PSA", StringComparison.Ordinal)
            ? NormalizePsalmsMainTitle(NormalizePsalmsChapterAndIntroMarkers(segment.Lines, preserveVerseMarkers))
            : segment.Lines;

        var hasId = segment.Lines.Any(line => line.StartsWith("\\id ", StringComparison.Ordinal));
        if (hasId)
        {
            return segmentLines;
        }

        if (string.IsNullOrWhiteSpace(bookId))
        {
            return segmentLines;
        }

        var displayTitle = ExtractPreferredDisplayTitle(segment, bookId);
        var lines = new List<string>();
        if (string.Equals(bookId, "PSA", StringComparison.Ordinal))
        {
            var psaTitle = Regex.Replace(displayTitle, "\\s*\\(.*\\)\\s*$", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(psaTitle))
            {
                psaTitle = ProfileContext.Current.PsaMainTitle;
            }

            lines.Add($"\\id psa {MapResourceIdToHeaderLabel("reg")}");
            lines.Add("\\ide usfm");
            lines.Add($"\\h {psaTitle}");
            lines.Add($"\\toc1 {psaTitle}");
            lines.Add($"\\toc2 {psaTitle}");
            lines.Add("\\toc3 psa");
        }
        else
        {
            lines.Add($"\\id {bookId.ToLowerInvariant()} {MapResourceIdToHeaderLabel("reg")}");
            lines.Add("\\ide usfm");
            lines.Add($"\\h {displayTitle}");
            lines.Add($"\\toc1 {displayTitle}");
            lines.Add($"\\toc2 {displayTitle}");
            lines.Add($"\\toc3 {bookId.ToLowerInvariant()}");
        }

        lines.AddRange(segmentLines);
        return lines;
    }

    static IReadOnlyList<string> EnsureSplitBookHeadersWithFallbackId(BookSegment segment, bool preserveVerseMarkers, string? fallbackBookId, string resourceId)
    {
        var lines = EnsureSplitBookHeaders(segment, preserveVerseMarkers).ToList();
        if (lines.Any(line => line.StartsWith("\\id ", StringComparison.Ordinal)))
        {
            ReplaceHeaderIdLine(lines, resourceId);
            return lines;
        }

        if (string.IsNullOrWhiteSpace(fallbackBookId))
        {
            return lines;
        }

        var displayTitle = ExtractPreferredDisplayTitle(segment, fallbackBookId);
        var header = new List<string>
    {
        $"\\id {fallbackBookId.ToLowerInvariant()} {MapResourceIdToHeaderLabel(resourceId)}",
        "\\ide usfm",
        $"\\h {displayTitle}",
        $"\\toc1 {displayTitle}",
        $"\\toc2 {displayTitle}",
        $"\\toc3 {fallbackBookId.ToLowerInvariant()}"
    };
        header.AddRange(lines);
        return header;
    }

    static void ReplaceHeaderIdLine(List<string> lines, string resourceId)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("\\id ", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                lines[i] = $"\\id {parts[1].ToLowerInvariant()} {MapResourceIdToHeaderLabel(resourceId)}";
            }

            return;
        }
    }

    static string ExtractPreferredDisplayTitle(BookSegment segment, string? fallbackBookId)
    {
        foreach (var line in segment.Lines)
        {
            if (line.StartsWith("\\mt ", StringComparison.Ordinal))
            {
                var title = line[4..].Trim();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(segment.Title))
        {
            return segment.Title.Trim();
        }

        return fallbackBookId ?? string.Empty;
    }

    static string BuildUsfmFileName(string? bookId, string? langCode, string resourceId, string? producerTag, int fallbackIndex)
    {
        var normalizedBook = string.IsNullOrWhiteSpace(bookId)
            ? $"{fallbackIndex:000}"
            : SanitizeFileStem(bookId).ToLowerInvariant();
        var normalizedLang = string.IsNullOrWhiteSpace(langCode)
            ? "und"
            : SanitizeFileStem(langCode).ToLowerInvariant();
        var normalizedResourceId = string.IsNullOrWhiteSpace(resourceId)
            ? "reg"
            : SanitizeFileStem(resourceId).ToLowerInvariant();
        var normalizedProducer = string.IsNullOrWhiteSpace(producerTag)
            ? string.Empty
            : "_" + SanitizeFileStem(producerTag).ToLowerInvariant();

        return $"{normalizedLang}_{normalizedBook}_text_{normalizedResourceId}{normalizedProducer}.usfm";
    }

    static string MapResourceIdToHeaderLabel(string resourceId)
    {
        return string.Equals(resourceId, "reg", StringComparison.OrdinalIgnoreCase)
            ? "Regular"
            : string.IsNullOrWhiteSpace(resourceId)
                ? "Regular"
                : resourceId.Trim();
    }

    static List<string> NormalizePsalmsMainTitle(IReadOnlyList<string> lines)
    {
        var normalized = new List<string>(lines.Count);
        var mtReplaced = false;
        var mainTitle = string.IsNullOrWhiteSpace(ProfileContext.Current.PsaMainTitle)
            ? "ПСАЛТЫРЬ"
            : ProfileContext.Current.PsaMainTitle;
        foreach (var line in lines)
        {
            if (!mtReplaced && line.StartsWith("\\mt ", StringComparison.Ordinal))
            {
                normalized.Add($"\\mt {mainTitle}");
                mtReplaced = true;
                continue;
            }

            normalized.Add(line);
        }

        return normalized;
    }

    static List<string> NormalizePsalmsChapterAndIntroMarkers(IReadOnlyList<string> lines, bool preserveVerseMarkers)
    {
        // Psalms in this source often use "\cl Псалом N" as chapter starts.
        // Convert them to real chapter markers and keep chapter intros as \d lines.
        var normalized = new List<string>(lines.Count);
        var chapterKeywords = ProfileContext.Current.PsalmChapterKeywords;
        var keywordPattern = string.Join("|", chapterKeywords.Select(Regex.Escape));
        var psalmChapterRegex = new Regex(
            $"^\\\\cl\\s+(?:{keywordPattern})\\s+(\\d+)\\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var chapterMarkerLabel = chapterKeywords.FirstOrDefault() ?? "Псалом";

        var chapterStarted = false;
        var seenVerseInChapter = false;

        foreach (var line in lines)
        {
            var chapterMatch = psalmChapterRegex.Match(line);
            if (chapterMatch.Success && int.TryParse(chapterMatch.Groups[1].Value, out var chapterNumber) && chapterNumber > 0)
            {
                normalized.Add($"\\c {chapterNumber}");
                normalized.Add($"\\cl {chapterMarkerLabel} {chapterNumber}");
                chapterStarted = true;
                seenVerseInChapter = false;
                continue;
            }

            if (line.StartsWith("\\c ", StringComparison.Ordinal))
            {
                chapterStarted = true;
                seenVerseInChapter = false;
                normalized.Add(line);
                continue;
            }

            if (line.StartsWith("\\v ", StringComparison.Ordinal))
            {
                seenVerseInChapter = true;
                normalized.Add(line);
                continue;
            }

            if (chapterStarted && !seenVerseInChapter && line.StartsWith("\\cl ", StringComparison.Ordinal))
            {
                var intro = line[4..].Trim();
                if (!string.IsNullOrWhiteSpace(intro))
                {
                    normalized.Add($"\\d {intro}");
                    continue;
                }
            }

            normalized.Add(line);
        }

        return preserveVerseMarkers ? normalized : NormalizePsalmsBttwStyle(normalized);
    }

    static List<string> NormalizePsalmsBttwStyle(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);
        var i = 0;

        while (i < lines.Count)
        {
            if (!lines[i].StartsWith("\\c ", StringComparison.Ordinal))
            {
                result.Add(lines[i]);
                i++;
                continue;
            }

            var chapterHeader = new List<string>();
            chapterHeader.Add(lines[i]);
            i++;

            while (i < lines.Count && lines[i].StartsWith("\\cl ", StringComparison.Ordinal))
            {
                chapterHeader.Add(lines[i]);
                i++;
            }

            var chapterBody = new List<string>();
            while (i < lines.Count && !lines[i].StartsWith("\\c ", StringComparison.Ordinal))
            {
                chapterBody.Add(lines[i]);
                i++;
            }

            var normalizedChapter = NormalizePsalmsChapterBody(chapterBody);
            result.AddRange(chapterHeader);
            result.AddRange(normalizedChapter);
        }

        return result;
    }

    static List<string> NormalizePsalmsChapterBody(IReadOnlyList<string> chapterBody)
    {
        var introLines = new List<string>();
        var verseLines = new List<string>();
        var passthrough = new List<string>();

        foreach (var line in chapterBody)
        {
            if (line == "\\p")
            {
                continue;
            }

            if (line.StartsWith("\\d ", StringComparison.Ordinal))
            {
                var intro = line[3..].Trim();
                if (!string.IsNullOrWhiteSpace(intro))
                {
                    introLines.Add(intro);
                }
                continue;
            }

            if (line.StartsWith("\\v ", StringComparison.Ordinal))
            {
                verseLines.Add(line);
                continue;
            }

            if (line.StartsWith("\\", StringComparison.Ordinal))
            {
                passthrough.Add(line);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                introLines.Add(line.Trim());
            }
        }

        if (introLines.Count == 0 && verseLines.Count > 1 && TryParseVerseLine(verseLines[0], out var firstVerse, out var firstText))
        {
            if (firstVerse == 1 && IsLikelyPsalmSuperscription(firstText))
            {
                introLines.Add(firstText);
                verseLines.RemoveAt(0);
                for (var j = 0; j < verseLines.Count; j++)
                {
                    if (TryParseVerseLine(verseLines[j], out var number, out var text) && number > 1)
                    {
                        verseLines[j] = $"\\v {number - 1} {text}";
                    }
                }
            }
        }

        var normalized = new List<string>();
        normalized.Add("\\p");
        foreach (var intro in introLines)
        {
            normalized.Add($"\\d {intro}");
        }

        normalized.AddRange(verseLines);
        normalized.AddRange(passthrough);
        return normalized;
    }

    static bool TryParseVerseLine(string line, out int verseNumber, out string verseText)
    {
        verseNumber = 0;
        verseText = string.Empty;
        var match = Regex.Match(line, "^\\\\v\\s+(\\d+)\\s+(.*)$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out verseNumber))
        {
            return false;
        }

        verseText = match.Groups[2].Value.Trim();
        return true;
    }

    static bool IsLikelyPsalmSuperscription(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.TrimStart();
        return value.StartsWith("[", StringComparison.OrdinalIgnoreCase)
            || ProfileContext.Current.PsalmSuperscriptionStarts.Any(prefix =>
                value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    static string? InferBookId(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var t = title.ToLowerInvariant();
        var normalizedTitle = NormalizeForAliasMatch(t);
        foreach (var kvp in ProfileContext.Current.BookAliases)
        {
            foreach (var alias in kvp.Value)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                if (t.Contains(alias, StringComparison.OrdinalIgnoreCase))
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

    static string NormalizeForAliasMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalizedDigits = value.Normalize(NormalizationForm.FormKC)
            .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
            .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9')
            .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
            .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9');

        var sb = new StringBuilder(normalizedDigits.Length);
        foreach (var ch in normalizedDigits)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    static List<BookSegment> SplitIntoBookSegments(IReadOnlyList<string> lines)
    {
        var segments = new List<BookSegment>();
        var current = new List<string>();
        string currentTitle = "book";

        foreach (var line in lines)
        {
            if (current.Count > 0 && TrySplitInlineBookHeadingLine(line, out var currentLine, out var nextTitle))
            {
                if (!string.IsNullOrWhiteSpace(currentLine))
                {
                    current.Add(currentLine);
                }

                segments.Add(new BookSegment(currentTitle, current));
                current = new List<string>();
                currentTitle = nextTitle;
                continue;
            }

            if (current.Count > 0 && IsLikelyStandaloneBookHeading(line))
            {
                segments.Add(new BookSegment(currentTitle, current));
                current = new List<string>();
                currentTitle = line.Trim();
                continue;
            }

            if (line.StartsWith("\\mt ", StringComparison.Ordinal) && current.Count > 0)
            {
                segments.Add(new BookSegment(currentTitle, current));
                current = new List<string>();
            }

            if (line.StartsWith("\\mt ", StringComparison.Ordinal))
            {
                currentTitle = line.Substring(4).Trim();
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            segments.Add(new BookSegment(currentTitle, current));
        }

        if (segments.Count == 0)
        {
            segments.Add(new BookSegment("book", lines.ToList()));
        }

        return segments;
    }

    static bool TrySplitInlineBookHeadingLine(string line, out string currentLine, out string nextTitle)
    {
        currentLine = line;
        nextTitle = string.Empty;

        if (!line.StartsWith("\\v ", StringComparison.Ordinal))
        {
            return false;
        }

        var splitIndex = FindInlineBookHeadingStartIndex(line);

        if (splitIndex <= 0)
        {
            return false;
        }

        var candidate = line[(splitIndex + 1)..].Trim();
        if (!IsLikelyBookHeadingText(candidate))
        {
            return false;
        }

        currentLine = line[..splitIndex].TrimEnd();
        nextTitle = candidate;
        return true;
    }

    static string ResolveSplitOutputDirectory(string outputTarget)
    {
        var fullPath = Path.GetFullPath(outputTarget);

        if (Directory.Exists(fullPath))
        {
            return fullPath;
        }

        if (!Path.HasExtension(fullPath)
            || outputTarget.EndsWith(Path.DirectorySeparatorChar)
            || outputTarget.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return fullPath;
        }

        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(directory, stem + "_books");
    }

    static string SanitizeFileStem(string raw)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? "book" : raw.Trim();

        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value
            .Where(ch => !invalidChars.Contains(ch))
            .Select(ch => char.IsWhiteSpace(ch) ? '-' : ch)
            .ToArray();

        var sanitized = new string(chars);
        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        sanitized = sanitized.Trim('-', '.');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "book";
        }

        return sanitized.Length > 64 ? sanitized[..64] : sanitized;
    }

    static void WriteIssueReport(
        string reportPath,
        string inputDocxPath,
        bool splitBooks,
        string outputTarget,
        IReadOnlyList<ContractIssue> issues)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sb = new StringBuilder();
        sb.AppendLine("USFM Contract CLI Report");
        sb.AppendLine($"Timestamp: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Input DOCX: {Path.GetFullPath(inputDocxPath)}");
        sb.AppendLine($"Output Target: {Path.GetFullPath(outputTarget)}");
        sb.AppendLine($"Split Books: {(splitBooks ? "yes" : "no")}");
        sb.AppendLine($"Issue Count: {issues.Count}");
        sb.AppendLine();

        if (issues.Count > 0)
        {
            sb.AppendLine("Issue Summary:");
            foreach (var group in issues.GroupBy(i => i.Code).OrderBy(g => g.Key))
            {
                sb.AppendLine($"- {group.Key}: {group.Count()}");
            }

            sb.AppendLine();
            sb.AppendLine("Issues:");
            foreach (var issue in issues)
            {
                sb.AppendLine($"[{issue.Severity}] {issue.Code}: {issue.Message}");
            }
        }
        else
        {
            sb.AppendLine("No issues found.");
        }

        File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    static void PrintUsage()
    {
        StandardOutput.WriteLine("USFM Contract CLI");
        StandardOutput.WriteLine();
        StandardOutput.WriteLine("Usage:");
        StandardOutput.WriteLine("  validate-marker <marker> [strict|permissive]");
        StandardOutput.WriteLine("  validate-styles <paragraphStyleCsv> <characterStyleCsv> [strict|permissive]");
        StandardOutput.WriteLine("  docx-to-usfm <input.docx> <output.usfm|output-dir> [strict|permissive] [--report <report.txt>] [--split-books] [--book <id>] [--books <id1,id2,...>] [--profile <id>] [--canon <protestant-ot|catholic-ot|orthodox-ot|protestant-nt|catholic-nt|orthodox-nt>] [--lang-code <lll>] [--resource-id <reg>] [--producer-tag <tag>] [--preserve-verse-markers] [--verse-txt <verses.txt>]");
        StandardOutput.WriteLine();
        StandardOutput.WriteLine("Examples:");
        StandardOutput.WriteLine("  validate-marker \\q strict");
        StandardOutput.WriteLine("  validate-styles USFM_P,USFM_MT USFM_V,USFM_TX strict");
        StandardOutput.WriteLine("  docx-to-usfm input.docx output.usfm permissive --report report.txt");
        StandardOutput.WriteLine("  docx-to-usfm input.docx output_dir permissive --split-books --report report.txt");
        StandardOutput.WriteLine("  docx-to-usfm input.docx output_dir permissive --split-books --book PSA");
        StandardOutput.WriteLine("  docx-to-usfm input.docx output_dir permissive --split-books --books PSA,PRO,ECC");
        StandardOutput.WriteLine("  docx-to-usfm input.docx output_dir permissive --split-books --book PSA --profile global-starter");
        StandardOutput.WriteLine("  docx-to-usfm input.docx output_dir permissive --split-books --book PSA --canon orthodox-ot");
        StandardOutput.WriteLine("  docx-to-usfm input.docx output_dir permissive --split-books --book LEV --lang-code skr --resource-id reg --producer-tag uisprem");
        StandardOutput.WriteLine("  docx-to-usfm input.docx output_dir permissive --split-books --book PSA --preserve-verse-markers");
        StandardOutput.WriteLine("  docx-to-usfm input.docx output_dir permissive --split-books --book GEN --verse-txt gen-verses.txt");
    }

    static int FindInlineBookHeadingStartIndex(string line)
    {
        var splitIndex = -1;
        foreach (var phrase in ProfileContext.Current.InlineSplitPhrases)
        {
            var idx = line.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
            if (idx <= 0)
            {
                continue;
            }

            if (splitIndex == -1 || idx < splitIndex)
            {
                splitIndex = idx;
            }
        }

        return splitIndex;
    }
}
readonly record struct BookSegment(string Title, List<string> Lines);

static class ProfileContext
{
    public static LanguageProfile Current { get; set; } = LanguageProfile.Default;
}

sealed record LanguageProfile(
    string Id,
    string Name,
    Dictionary<string, List<string>> BookAliases,
    List<string> HeadingPrefixes,
    List<string> InlineSplitPhrases,
    List<string> PsalmChapterKeywords,
    List<string> PsalmSuperscriptionStarts,
    string PsaMainTitle)
{
    public static LanguageProfile Default => new(
        "default-ru-en",
        "Default RU/EN",
        new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GEN"] = new() { "genesis", "бытие" },
            ["EXO"] = new() { "exodus", "исход" },
            ["LEV"] = new() { "leviticus", "левит" },
            ["NUM"] = new() { "numbers", "числа" },
            ["DEU"] = new() { "deuteronomy", "второзаконие" },
            ["JOS"] = new() { "joshua", "иисуса навина" },
            ["JDG"] = new() { "judges", "судей" },
            ["RUT"] = new() { "ruth", "руфь" },
            ["1SA"] = new() { "1 samuel", "1 книга самуила" },
            ["2SA"] = new() { "2 samuel", "вторая книга царств" },
            ["1KI"] = new() { "1 kings", "3 царств" },
            ["2KI"] = new() { "2 kings", "4 царств" },
            ["1CH"] = new() { "1 chronicles", "1 книга паралипоменон" },
            ["2CH"] = new() { "2 chronicles", "вторая книга паралипоменон" },
            ["EZR"] = new() { "ezra", "ездры" },
            ["NEH"] = new() { "nehemiah", "неемии" },
            ["EST"] = new() { "esther", "эсфирь" },
            ["JOB"] = new() { "job", "иова" },
            ["PSA"] = new() { "psalm", "псалт" },
            ["PRO"] = new() { "proverbs", "притчи" },
            ["ECC"] = new() { "ecclesiastes", "qoheleth", "екклесиаст", "екклезиаст", "экклесиаст", "экклезиаст" }
        },
        new() { "Книга ", "Первая книга ", "Вторая книга ", "Третья книга ", "Четвертая книга ", "Четвёртая книга ", "Книга пророка ", "Пророк ", "Book ", "First Book ", "Second Book ", "Third Book ", "Fourth Book " },
        new() { " Книга ", " Первая книга ", " Вторая книга ", " Третья книга ", " Четвертая книга ", " Четвёртая книга ", " Книга пророка ", " Book ", " First Book ", " Second Book ", " Third Book ", " Fourth Book " },
        new() { "Псалом", "Psalm" },
        new() { "Псалом ", "Плачевная песнь", "Начальнику хора", "Песнь", "Учение", "Молитва", "Аллилуйя", "О Соломоне" },
        "ПСАЛТЫРЬ");
}

sealed class LanguageProfileFile
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public Dictionary<string, List<string>>? BookAliases { get; set; }
    public List<string>? HeadingPrefixes { get; set; }
    public List<string>? InlineSplitPhrases { get; set; }
    public List<string>? PsalmChapterKeywords { get; set; }
    public List<string>? PsalmSuperscriptionStarts { get; set; }
    public string? PsaMainTitle { get; set; }
}

static class LanguageProfileLoader
{
    public static LanguageProfile LoadDefault()
    {
        if (TryResolve("global-starter", out var profile, out _))
        {
            return profile;
        }

        if (TryResolve(null, out profile, out _))
        {
            return profile;
        }

        return LanguageProfile.Default;
    }

    public static bool TryResolve(string? profileId, out LanguageProfile profile, out string error)
    {
        var loaded = LoadAllFromDisk();
        if (loaded.Count == 0)
        {
            profile = LanguageProfile.Default;
            error = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(profileId))
        {
            profile = loaded[0];
            error = string.Empty;
            return true;
        }

        var selected = loaded.FirstOrDefault(p =>
            string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));

        if (selected is null)
        {
            var available = string.Join(", ", loaded.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase));
            error = $"Unknown profile '{profileId}'. Available profiles: {available}.";
            profile = LanguageProfile.Default;
            return false;
        }

        profile = selected;
        error = string.Empty;
        return true;
    }

    private static List<LanguageProfile> LoadAllFromDisk()
    {
        var profiles = new List<LanguageProfile>();
        foreach (var dir in CandidateProfileDirectories())
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var raw = File.ReadAllText(file);
                    var parsed = JsonSerializer.Deserialize<LanguageProfileFile>(raw, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                    if (parsed is null || parsed.BookAliases is null || parsed.BookAliases.Count == 0)
                    {
                        continue;
                    }

                    var id = string.IsNullOrWhiteSpace(parsed.Id)
                        ? Path.GetFileNameWithoutExtension(file)
                        : parsed.Id.Trim();

                    profiles.Add(new LanguageProfile(
                        id,
                        parsed.Name?.Trim() ?? id,
                        NormalizeBookAliases(parsed.BookAliases),
                        NormalizeList(parsed.HeadingPrefixes, LanguageProfile.Default.HeadingPrefixes),
                        NormalizeList(parsed.InlineSplitPhrases, LanguageProfile.Default.InlineSplitPhrases),
                        NormalizeList(parsed.PsalmChapterKeywords, LanguageProfile.Default.PsalmChapterKeywords),
                        NormalizeList(parsed.PsalmSuperscriptionStarts, LanguageProfile.Default.PsalmSuperscriptionStarts),
                        string.IsNullOrWhiteSpace(parsed.PsaMainTitle) ? LanguageProfile.Default.PsaMainTitle : parsed.PsaMainTitle.Trim()));
                }
                catch
                {
                    // Ignore malformed profile files and continue loading others.
                }
            }
        }

        return profiles
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static IEnumerable<string> CandidateProfileDirectories()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Directory.GetCurrentDirectory(), "language-profiles"),
            Path.Combine(AppContext.BaseDirectory, "language-profiles")
        };

        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 5 && cursor is not null; i++)
        {
            dirs.Add(Path.Combine(cursor.FullName, "language-profiles"));
            cursor = cursor.Parent;
        }

        return dirs;
    }

    private static Dictionary<string, List<string>> NormalizeBookAliases(Dictionary<string, List<string>> source)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in source)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
            {
                continue;
            }

            var values = (kvp.Value ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (values.Count == 0)
            {
                continue;
            }

            map[kvp.Key.Trim().ToUpperInvariant()] = values;
        }

        return map;
    }

    private static List<string> NormalizeList(List<string>? source, IReadOnlyList<string> fallback)
    {
        var values = (source ?? fallback.ToList())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return values.Count == 0 ? fallback.ToList() : values;
    }
}
