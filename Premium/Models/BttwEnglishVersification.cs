using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace UsfmIntegrityStudio.Models;

internal static class BttwEnglishVersification
{
    private const string ProfileId = "bttw-en-US-v1";
    private static readonly Lazy<IReadOnlyDictionary<string, int[]>> VerseCounts = new(Load);

    internal static IReadOnlyDictionary<string, int[]> OldTestamentVerseCounts => VerseCounts.Value;

    internal static bool TryGetChapterVerseCounts(string bookId, out int[] chapterVerseCounts)
    {
        if (VerseCounts.Value.TryGetValue(bookId.Trim().ToUpperInvariant(), out var counts))
        {
            chapterVerseCounts = counts;
            return true;
        }

        chapterVerseCounts = [];
        return false;
    }

    private static IReadOnlyDictionary<string, int[]> Load()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "ConverterRuntime",
            "versification",
            "bttw-en-US-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!string.Equals(root.GetProperty("profile").GetString(), ProfileId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected BTTW versification profile in {path}.");
        }

        var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var book in root.GetProperty("maxVerses").EnumerateObject())
        {
            var counts = book.Value.EnumerateArray().Select(value => value.GetInt32()).ToArray();
            if (counts.Length == 0 || counts.Any(count => count <= 0))
            {
                throw new InvalidDataException($"Invalid BTTW verse counts for {book.Name}.");
            }

            result.Add(book.Name.ToUpperInvariant(), counts);
        }

        if (result.Count != 39)
        {
            throw new InvalidDataException($"BTTW OT versification must contain 39 books; found {result.Count}.");
        }

        return result;
    }
}
