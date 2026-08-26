using System;
using System.Collections.Generic;

namespace UsfmConverter.Contracts
{
    public enum ContractMode
    {
        Strict,
        Permissive
    }

    public enum Severity
    {
        Warning,
        Error
    }

    public sealed record ContractIssue(Severity Severity, string Code, string Message);

    public static class UsfmDocxContractV1
    {
        public const string Version = "USFM-DOCX/v1";

        // Supported marker inventory aligned with USFM 2.4 reference.
        public static readonly ISet<string> SupportedMarkers = new HashSet<string>(StringComparer.Ordinal)
        {
            "\\add",
            "\\add*",
            "\\b",
            "\\bd",
            "\\bd*",
            "\\bdit",
            "\\bdit*",
            "\\bk",
            "\\bk*",
            "\\c",
            "\\ca",
            "\\ca*",
            "\\cat",
            "\\cat*",
            "\\cd",
            "\\cl",
            "\\cls",
            "\\cp",
            "\\d",
            "\\dc",
            "\\dc*",
            "\\e",
            "\\ef",
            "\\ef*",
            "\\em",
            "\\em*",
            "\\esb",
            "\\esbe",
            "\\ex",
            "\\ex*",
            "\\f",
            "\\f*",
            "\\fdc",
            "\\fdc*",
            "\\fe",
            "\\fe*",
            "\\fig",
            "\\fig*",
            "\\fk",
            "\\fk*",
            "\\fl",
            "\\fm",
            "\\fm*",
            "\\fp",
            "\\fq",
            "\\fq*",
            "\\fqa",
            "\\fr",
            "\\fr*",
            "\\fs",
            "\\ft",
            "\\fv",
            "\\fv*",
            "\\h",
            "\\i",
            "\\ib",
            "\\id",
            "\\ide",
            "\\ie",
            "\\iex",
            "\\ili",
            "\\ili1",
            "\\im",
            "\\imi",
            "\\imq",
            "\\imt",
            "\\imt1",
            "\\imte",
            "\\imte1",
            "\\intro",
            "\\io",
            "\\io1",
            "\\ior",
            "\\ior*",
            "\\iot",
            "\\ip",
            "\\ipi",
            "\\ipq",
            "\\ipr",
            "\\iq",
            "\\iq1",
            "\\iq2",
            "\\iq3",
            "\\iqt",
            "\\iqt*",
            "\\is",
            "\\is1",
            "\\it",
            "\\it*",
            "\\k",
            "\\k*",
            "\\li",
            "\\li1",
            "\\lit",
            "\\m",
            "\\maps",
            "\\marker",
            "\\marker1",
            "\\mi",
            "\\mr",
            "\\ms",
            "\\ms1",
            "\\mt",
            "\\mt1",
            "\\mt2",
            "\\mt3",
            "\\mte",
            "\\mte1",
            "\\mte2",
            "\\nb",
            "\\nd",
            "\\nd*",
            "\\ndx",
            "\\ndx*",
            "\\no",
            "\\no*",
            "\\ord",
            "\\ord*",
            "\\p",
            "\\pb",
            "\\pc",
            "\\pde",
            "\\pdi",
            "\\periph",
            "\\ph",
            "\\ph1",
            "\\pi",
            "\\pi1",
            "\\pm",
            "\\pmc",
            "\\pmo",
            "\\pmr",
            "\\pn",
            "\\pn*",
            "\\pr",
            "\\pro",
            "\\pro*",
            "\\ps",
            "\\q",
            "\\q1",
            "\\q2",
            "\\q3",
            "\\qa",
            "\\qac",
            "\\qac*",
            "\\qc",
            "\\qm",
            "\\qm1",
            "\\qm2",
            "\\qr",
            "\\qs",
            "\\qs*",
            "\\qt",
            "\\qt*",
            "\\r",
            "\\rem",
            "\\rq",
            "\\rq*",
            "\\s",
            "\\s1",
            "\\s2",
            "\\s3",
            "\\sc",
            "\\sc*",
            "\\sig",
            "\\sig*",
            "\\sls",
            "\\sls*",
            "\\sp",
            "\\sr",
            "\\sts",
            "\\tc",
            "\\tc1",
            "\\tc2",
            "\\tc3",
            "\\tcr",
            "\\tcr1",
            "\\tcr2",
            "\\tcr3",
            "\\tcr4",
            "\\th",
            "\\th1",
            "\\th2",
            "\\th3",
            "\\thr",
            "\\thr2",
            "\\thr3",
            "\\thr4",
            "\\tl",
            "\\tl*",
            "\\toc",
            "\\toc1",
            "\\toc2",
            "\\toc3",
            "\\tr",
            "\\usfm",
            "\\v",
            "\\va",
            "\\va*",
            "\\vp",
            "\\vp*",
            "\\w",
            "\\w*",
            "\\wg",
            "\\wg*",
            "\\wh",
            "\\wh*",
            "\\wj",
            "\\wj*",
            "\\wr",
            "\\wr*",
            "\\x",
            "\\x*",
            "\\xdc",
            "\\xdc*",
            "\\xk",
            "\\xnt",
            "\\xnt*",
            "\\xo",
            "\\xot",
            "\\xot*",
            "\\xq",
            "\\xt",
            "\\xt*",
            "\\z"
        };

        public static class ParagraphStyles
        {
            public const string Mt = "USFM_MT"; // \mt
            public const string Cl = "USFM_CL"; // \cl
            public const string P = "USFM_P";   // \p
        }

        public static class CharacterStyles
        {
            public const string VerseNumber = "USFM_V"; // \v number token
            public const string Text = "USFM_TX";       // standard text
            public const string FootnoteText = "USFM_FT"; // \ft
        }

        public static class CustomProperties
        {
            public const string Id = "USFM_ID";     // \id
            public const string Ide = "USFM_IDE";   // \ide
            public const string H = "USFM_H";       // \h
            public const string Toc1 = "USFM_TOC1"; // \toc1
            public const string Toc2 = "USFM_TOC2"; // \toc2
            public const string Toc3 = "USFM_TOC3"; // \toc3
        }

        public static IReadOnlyDictionary<string, string> MarkerToParagraphStyle { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["\\mt"] = ParagraphStyles.Mt,
                ["\\cl"] = ParagraphStyles.Cl,
                ["\\p"] = ParagraphStyles.P
            };

        public static IReadOnlyDictionary<string, string> MarkerToCharacterStyle { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["\\v"] = CharacterStyles.VerseNumber,
                ["\\ft"] = CharacterStyles.FootnoteText
            };

        public static IReadOnlyDictionary<string, string> MarkerToCustomProperty { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["\\id"] = CustomProperties.Id,
                ["\\ide"] = CustomProperties.Ide,
                ["\\h"] = CustomProperties.H,
                ["\\toc1"] = CustomProperties.Toc1,
                ["\\toc2"] = CustomProperties.Toc2,
                ["\\toc3"] = CustomProperties.Toc3
            };

        public static IEnumerable<ContractIssue> ValidateStyles(
            ISet<string> paragraphStyleIds,
            ISet<string> characterStyleIds,
            ContractMode mode)
        {
            if (!paragraphStyleIds.Contains(ParagraphStyles.P))
            {
                yield return Issue(mode, "STYLE_MISSING_USFM_P", "Required paragraph style USFM_P is missing.");
            }

            if (!characterStyleIds.Contains(CharacterStyles.VerseNumber))
            {
                yield return Issue(mode, "STYLE_MISSING_USFM_V", "Required character style USFM_V is missing.");
            }
        }

        public static IEnumerable<ContractIssue> ValidateMarker(string marker, ContractMode mode)
        {
            if (marker.StartsWith("\\z", StringComparison.Ordinal))
            {
                yield return new ContractIssue(
                    Severity.Warning,
                    "MARKER_PRIVATE_NAMESPACE",
                    $"Marker '{marker}' is in private namespace and may not be portable across USFM tools.");
                yield break;
            }

            if (!SupportedMarkers.Contains(marker))
            {
                yield return Issue(
                    mode,
                    "MARKER_UNSUPPORTED",
                    $"Marker '{marker}' is not supported in {Version}.");
            }
        }

        private static ContractIssue Issue(ContractMode mode, string code, string message)
        {
            return mode == ContractMode.Strict
                ? new ContractIssue(Severity.Error, code, message)
                : new ContractIssue(Severity.Warning, code, message);
        }
    }
}
