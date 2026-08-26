using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UsfmIntegrityStudio.ConverterRuntime;

namespace UsfmIntegrityStudio.Models;

public sealed record DocxConversionRequest(
    string InputDocxPath,
    string OutputDirectory,
    string ReportPath,
    string Mode,
    string CanonToken,
    string LanguageCode,
    IReadOnlyCollection<string> SelectedBookIds,
    bool PreserveVerseMarkers);

public sealed record DocxConversionExecution(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    IReadOnlyList<string> GeneratedUsfmPaths)
{
    public bool GeneratedOutput => GeneratedUsfmPaths.Count > 0;
    public bool CompletedWithWarnings => ExitCode == 2 && GeneratedOutput;
}

public static class DocxConversionService
{
    public static DocxConversionExecution Execute(DocxConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var selectedBookIds = request.SelectedBookIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var args = new List<string>
        {
            "docx-to-usfm",
            request.InputDocxPath,
            request.OutputDirectory,
            request.Mode,
            "--split-books"
        };
        if (selectedBookIds.Length == 1)
        {
            args.AddRange(["--book", selectedBookIds[0]]);
        }
        else if (selectedBookIds.Length > 1)
        {
            args.AddRange(["--books", string.Join(',', selectedBookIds)]);
        }

        args.AddRange([
            "--canon", request.CanonToken,
            "--profile", "global-starter",
            "--lang-code", request.LanguageCode,
            "--resource-id", "reg",
            "--producer-tag", "uisprem",
            "--report", request.ReportPath
        ]);
        if (request.PreserveVerseMarkers)
        {
            args.Add("--preserve-verse-markers");
        }

        var before = CaptureUsfmFingerprints(request.OutputDirectory);
        var execution = BundledUsfmContractRuntime.Execute(args.ToArray());
        var reportedPaths = ExtractReportedUsfmPaths(execution.StandardOutput);
        var generatedPaths = reportedPaths.Count > 0
            ? reportedPaths
            : Directory.Exists(request.OutputDirectory)
                ? Directory.GetFiles(request.OutputDirectory, "*.usfm", SearchOption.TopDirectoryOnly)
                .Where(path => !before.TryGetValue(path, out var fingerprint)
                    || !string.Equals(fingerprint, ComputeFingerprint(path), StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
                : [];

        return new DocxConversionExecution(
            execution.ExitCode,
            execution.StandardOutput,
            execution.StandardError,
            generatedPaths);
    }

    private static Dictionary<string, string> CaptureUsfmFingerprints(string outputDirectory)
    {
        return Directory.Exists(outputDirectory)
            ? Directory.GetFiles(outputDirectory, "*.usfm", SearchOption.TopDirectoryOnly)
                .ToDictionary(path => path, ComputeFingerprint, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ExtractReportedUsfmPaths(string standardOutput)
    {
        const string prefix = "Generated USFM file: ";
        return standardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => Path.GetFullPath(line[prefix.Length..].Trim()))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ComputeFingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
