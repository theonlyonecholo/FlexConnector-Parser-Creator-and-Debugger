# Changelog

## 2.7.0 — 2026-10-20

- Show the raw routing ID token and its extraction status above the submessage body.
- Keep URLs and email addresses as plain editor text so capture highlights can split them correctly.
- Add searchable ArcSight field selection to submessage mappings, sharing the persistent custom field catalog.
- Search submessage IDs, patterns and mapping values while preserving draft edits and original indices.
- Check token/field behavior against the supplied FlexConnector 8.4.3 guide (October 2023).

## 2.6.0 — 2026-10-20

- Detect unsupported parser families and report duplicate property keys with source lines.
- Add an offline event preview with supported conversions, conditional maps, submessage assignments and a field-write trace.
- Resolve explicit map dependencies and preview exact-match CSV processors in order, including overwrite rules.
- Add reusable JSON expected-event tests with PASS, FAIL and INCOMPLETE results.
- Include worked parser, CSV maps and expected-event fixtures. See docs/EVENT_PIPELINE.md for supported behavior and limitations.

## 2.5.0 — 2026-10-20

- Show documented event-field types, lengths, ranges, restrictions and source/version in the mapping catalog.
- Add persistent local field definitions and overrides, with restoration of built-ins.
- Use locally defined types in mapping diagnostics without changing existing parser assignments.
- Update metadata from the public FlexConnector CE 24.3 guide, including oldFileSize/oldFileType and 4000-length custom strings.
- Preserve the regex insertion point during caret-driven element highlighting and asynchronous preview refresh in both editors.
- Document a public Sysmon parser review and prioritized compatibility improvements.

## 2.4.0 — 2026-10-20

- Add a shared persistent local regex preset library with create, edit, delete and full-selection validation in the main and submessage editors.
- Include editable IPv4, IPv6, ASCII FQDN, MAC, integer and UUID presets with documented scope.
- Add one-step undoable selection replacement and capturing-group wrapping.

## 2.3.5 — 2026-10-20

- Enable automatic submessage previews by default, including regex edits and pattern switching, and restore caret-to-capture highlighting after asynchronous refresh.
- Document default definitions with ordered pattern alternatives and verify first-match, no-match and known-ID behavior.
- Expose local submessage limits and enforce the per-definition pattern limit when exporting unfinished drafts as well as validated configurations.

## 2.3.4 — 2026-10-20

- Color selected log details by parser stage: green for matched extraction, red for a failed stage or unresolved routing token, and orange for partial or uncertain results.
- Explain partial-prefix estimates, missing suffixes and empty inputs without claiming an exact regex failure location.
- Keep capture selection in blue and compute partial previews only for the selected log, with cancellation on navigation.

## 2.3.3 — 2026-10-19

- Explain missing submessage routes in log-file details: routing token, captured ID, configured exact IDs, and why no pattern was tested.
- Clarify that message IDs are exact values rather than display labels.
- Add a Windows NetFt example with one default submessage and four regex alternatives, plus tab-delimited sample logs and routing regression coverage.

## 2.3.2 — 2026-10-19

- Keep the extracted body visible while editing or switching submessage patterns.
- Extract main tokens independently of invalid or unfinished submessage drafts; saving still validates all patterns.
- Add body and message-ID token dropdowns from declared main tokens, with automatic input refresh.
- Explain unavailable body values and cover initial loading, invalid patterns, and dropdown changes with regression tests.

## 2.3.1 — 2026-10-19

- Display the extracted submessage.token value as the submessage workbench input, with token-relative capture and partial-debug highlights.
- Keep the editable full event in an Original log tab and extract the configured token when the window opens or F5 is pressed.
- Clear missing/stale body previews and distinguish an empty captured value from failed extraction.
- Verify token selection against the local and official FlexConnector Developer's Guide and add regressions for body changes and failed main matching.

## 2.3.0 — 2026-10-19

- Preview matching regex prefixes when the complete regex fails, in both the main window and submessages.
- Highlight partial text in orange and navigate individual regex elements while preserving original-log offsets.
- Keep actual parser matching and routing separate from the explicitly labeled partial hypothesis.
- Bound prefix probes and cover nested groups, alternatives, quantifiers, anchors, trimming and cancellation with regressions.

## 2.2.1 — 2026-10-19

- Insert new properties beside their category and indexed siblings instead of at the end of the parser.
- Preserve existing submessage and extra processor positions during editor updates.
- Preserve unchanged raw lines and newline style, and avoid repeated processor-comment duplication.
- Cover tokens, nested submessages, event mapping, severity, processors and repeated saves with regression tests.

## 2.2.0 — 2026-10-19

- Redesigned the shared cross-platform submessage editor with persistent message/regex editors,
  separate capture, element, field, advanced-mapping and diagnostics tabs, and clear routing status.
- Added regex-element stepping, clickable capture previews, F5 and optional automatic preview.
- Added independent selected-pattern debugging without changing actual message-ID or pattern routing.
- Preserved full-log highlight offsets through trimming and long body captures; added regression coverage.

## 2.1.0 — 2026-10-19

- Added offline parser proposals from sample logs, with tokens, inferred event fields,
  ordered submessage alternatives, conventional textual severity mappings and sample coverage.
- Added raw token snippets and string-constant previews to event mapping.
- Added source-order regex element navigation with message highlights and explicit unsupported-syntax diagnostics.
- Added regression tests and a guide describing inference assumptions and debugging limits.

## 2.0.1 — 2026-10-14

- Added visible borders and inner padding to message, regex and configuration editors.
- Aligned text inputs, type selectors and buttons across native platforms.
- Kept token format builders on the same row and allocated more width to formats.
- Removed unnecessary input minimum widths that could overflow narrow grid columns.
- Widened the regex timeout input to prevent clipped values on Linux.
- Added native regression checks for wrapped regexes, compact viewports and token alignment.
- Validated the UI on Windows and Ubuntu 26.04 LTS under WSLg.

## 2.0.0 — 2026-10-14

- Added a native Avalonia desktop edition with Windows, Linux and macOS targets.
- Reused ParserCore for properties, regex debugging, token generation, event mapping,
  severity rules, extra processors and ordered submessage routing.
- Added colored message captures, regex group navigation, token snippets, format
  templates, 100-step undo/redo, offline English help and a log-file workbench.
- Added native UI regression tests and self-contained Linux x64 / Windows x64 packaging.
- Verified the native UI on Windows and Ubuntu 26.04 LTS x64 under WSLg.
- Kept the existing Windows WinForms solution available separately. macOS targets
  are available from source but have not been runtime-tested in this release.

## 1.1.0 — 2026-10-14

- Translated all application menus, dialogs, tooltips, accessibility descriptions,
  validation messages and test results into English.
- Translated all eleven searchable offline help topics and the user guide.
- Updated UI regression expectations and screenshots for the English interface.
- Set English framework diagnostic messages without changing the culture used
  for data or regular expressions. Native Windows dialogs follow Windows language settings.
- Prepared C# sources and a matching framework-dependent application package.

Requires Windows and .NET 10 Windows Desktop Runtime. Parser properties, token
names, user log content and ArcSight expressions are preserved as entered.
