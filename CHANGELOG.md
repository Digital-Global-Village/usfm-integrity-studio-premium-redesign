# Changelog

All notable changes to this project will be documented here.

## Unreleased

## 0.2.9 - 2026-09-30

### Fixed

- Use one BTTW-compatible English Protestant OT verse-count profile for DOCX
  scanning, standardization, and conversion validation across all 39 OT books.
- Accept Song of Solomon 6:13 while continuing to reject verses beyond the
  selected BTTW-compatible chapter ceiling.
- Read standalone numeric verse paragraphs as complete verse markers, preserving
  multi-digit numbers and attaching subsequent text to the correct verse.
- Recognize uppercase `\\V` verse markers during scanning and normalize them to
  canonical lowercase `\\v` markers in standardized DOCX copies.

### Verification

- Added full-profile checks for 39 books, 929 chapters, and 23,145 verses.
- Added SNG 6:13 acceptance, SNG 6:14 rejection, uppercase-marker, idempotence,
  and unchanged NT-versification regression coverage.
- Existing regression suite passed, including punctuation, RTL quote handling,
  project cleaning, chunk mapping, and non-destructive duplicate blocking.

- Added standalone multi-digit verse-number and continuation-text coverage.
- Verified the supplied SNG document produces all 117 verses in 8 chapters and
  a valid BTTW project package without duplicate or unmapped-paragraph findings.

## 0.2.8 - 2026-09-28

### Fixed

- Remove files named exactly `.DS_Store` from cleaned `.tstudio` packages,
  including package-root and nested chapter copies.
- Report the number of Finder metadata files removed without counting or
  rewriting scripture content.

### Verification

- Added synthetic package-root and nested `.DS_Store` regression fixtures.
- Confirmed similarly hidden non-target files remain byte-for-byte unchanged.
- Confirmed a second cleaning pass reports no additional metadata removal.

## 0.2.7 - 2026-09-28

### Fixed

- Remove standalone `\\p` markers accidentally persisted in non-front chapter
  title files while preserving the title text itself.
- Report reversed/loose verse-marker normalization only when reconstructed text
  actually differs from the input.
- Describe punctuation/spacing counts as normalized text lines, matching what
  the cleaner actually records.
- Collapse exactly doubled ASCII full stops, including spaced `. .` artifacts,
  without changing intentional three-dot ellipses.

### Verification

- Added synthetic regression coverage for title-marker removal, truthful
  normalization counts, doubled-period cleanup, ellipsis preservation, and
  second-pass idempotence.
- Out-of-range verses remain warning-only and are never renumbered or deleted.

## 0.2.6 - 2026-09-28

### Fixed

- Normalize Kalasha (`kls`) U+2019 and U+2018 word/accent markers to ASCII
  backticks without applying directional single-quote spacing.
- Repair the narrow spacing signatures previously introduced around those
  Kalasha markers while leaving other languages and straight quotes unchanged.

### Verification

- Added `.tstudio` regression coverage for `b’hi`, `bis’gai`, `d‘i`, and
  `Mul‘awa`, including exact backtick output and reported normalization counts.
- Existing Arabic-derived quote direction, embedded English quote preservation,
  control cleanup, chunk structure, verse numbering, and second-pass behavior
  remain covered by the full regression suite.

## 0.2.5 - 2026-09-26

### Fixed

- Remove `U+2060` word-joiner residue only when it occurs between a USFM verse
  marker and its verse text, including repeated residue separated by whitespace.
- Report and fail post-clean verification if verse-marker word-joiner residue
  remains in a standalone USFM file or BTTW project package.

### Verification

- Added synthetic Latin and RTL coverage for standalone USFM and `.tstudio`
  cleaning, removal counts, quote preservation, and second-pass idempotence.
- Canon tables, chunk boundaries, verse numbering, language metadata, DOCX
  conversion, and text outside the verified marker-residue signature are
  unchanged.

## 0.2.4 - 2026-09-23

### Added

- Display the public product version beside the main USFM Integrity Studio
  heading so testers can identify the running build immediately.
- Detect a selected BTTW project's target-language code from its embedded
  manifest without modifying the project.
- Generate a readable result-folder name from the selected source when the
  user leaves the result name blank, while preserving an explicitly entered
  name or language tag.

### Changed

- Use distinct workflow-guide colors for source selection, settings,
  processing, and review.
- Replace prefilled result-folder and language values with explanatory
  placeholders. Blank language metadata safely falls back to `und` only when
  output is generated.
- Accept two-letter, three-letter, and script-qualified language identifiers
  such as `ur`, `urd`, and `ur-PK`.
- Apply Arabic-derived straight-quote conversion only when Arabic-script text
  or compatible project language metadata supports it. Latin-script content,
  including `kls`, is preserved.

### Fixed

- Recognize Arabic Supplement, Arabic Extended, and Arabic Presentation Forms
  when selecting RTL quote direction.
- Avoid silently writing a conversion run over an existing result directory;
  a numbered sibling directory is selected instead.

### Verification

- Added regression coverage for empty workspace defaults, generated and custom
  result-folder names, language-tag preservation, script-neutral Urdu metadata,
  embedded Latin text, `kls` content, extended Arabic Unicode, and preservation
  of existing project-language metadata.

## 0.2.2 - 2026-09-14

### Added

- Reject recognized DOCX book-title mismatches and impossible explicit chapter
  or verse numbers before conversion output is written.
- Bundle the licensed UBS original-versification limits used by conversion
  preflight checks.

### Fixed

- Run BTTW project cleaning without cross-thread UI access failures.
- Clarify DOCX conversion and existing-file repair controls, including readable
  disabled states and output-destination guidance.
- Default new sessions to the Protestant NT canon profile.

## 0.2.1 - 2026-09-09

### DOCX detection groundwork

- Recognize exact OT/NT headings and book codes from the bundled converter alias
  profile; ambiguous normalized aliases do not choose a book automatically.
- Scan manual Word-break lines and attached explicit verse numbers. Report
  descending recognized verse markers with paragraph locations without sorting
  scripture; include duplicate/order warnings in canonical highlight reports.
- Fix digit duplication when one explicit verse number spans Word text runs.
- Add 347 profile-heading/code regression checks, sentence negatives, synthetic
  RTL line/order checks, and an optional private Romans acceptance test selected
  through UIS_PRIVATE_DOCX. No private document or local path is included.
- Scope: this is detection groundwork, not completion of the full workflow plan.
  Manual-selection context, universal before/after preservation checks, expanded
  session isolation and interactive BTTW round trips remain to be verified.
  Legacy same-line embedded-marker heuristics and nested Word containers are
  not replaced in this phase.

### Added

- Added Ecclesiastes verse-count validation and non-destructive diagnostics for
  missing chapter directories, repeated verse markers inside a chunk, and verse
  markers outside the selected chapter's canonical range.

### Fixed

- Reject malformed USFM-to-project packaging when chapter markers are duplicated
  or out of order, or verse markers are duplicated or out of order. Intentional
  partial and noncontiguous chapter selections remain supported.

## 0.2.0 - 2026-08-27

### Added

- Bundled the DOCX-to-USFM converter runtime and language profiles so packaged
  applications no longer depend on a neighboring CLI source project.
- Added a visible, locally persisted English source-USFM folder setting for
  source-aware project cleanup.
- Added regression coverage for bundled conversion, identical retries,
  stale-output exclusion, explicit source lookup, and BTTW project packaging.

### Changed

- Report converter exit code 2 as successful output with warnings.
- Package only USFM files explicitly generated by the current conversion run.
- Expanded DOCX standardization recovery for real-world chapter and verse
  formatting while preserving source files and explicit numbering.

### Fixed

- Restored DOCX-to-USFM and `.tstudio` generation in self-contained redesign
  packages.
- Prevented identical conversion retries from being misreported as producing no
  output.

## 0.1.0 - 2026-08-05

### Added

- Transparent **About & Verify** build identity and privacy information.
- AGPL, BTT-Writer provenance, third-party, privacy, security, contribution,
  trademark, and official-release policies.
- Locked NuGet dependency graph, advisory auditing, CI, Dependabot, and
  CODEOWNERS.
- GitHub branch protection and private vulnerability reporting.

### Security

- Pinned `Tmds.DBus.Protocol` to patched version 0.21.3 for
  GHSA-xrw6-gwf8-vvr9.
- Removed the Avalonia default application icon from UIS branding.

### Distribution

- Added reproducible macOS Apple Silicon/Intel, Windows x64, and Linux x64
  release packaging with official source-revision metadata.
- Added SHA-256 checksums, dependency-lock evidence, and legal notices to the
  release packages.
- Documented that initial packages are unsigned and not Apple-notarized.
