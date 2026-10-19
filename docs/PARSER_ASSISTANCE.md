# Offline parser suggestions and element debugging (2.1)

These features belong to the Avalonia cross-platform edition.

## Generate a parser from sample logs

1. Open **Suggest parser…** and paste 2–200 nonempty events, one per line.
2. Select **Generate and validate**. The application uses local structural heuristics; it does not call an AI API or upload samples.
3. Review both the proposed properties and coverage report.
4. Select **Use proposed parser**, then confirm replacement of the working draft. Nothing is written to disk until Save.
5. Test additional, previously unseen logs using **Test log file…**. Refine types/formats, mappings, submessages and severity in their existing editors.

The generator recognizes whitespace-separated fields, key=value fields and quoted values containing spaces. It preserves constant literals, generalizes variable fields, names tokens from keys, and suggests mappings for user, address, port, host, action, message, severity and event ID keys. Unknown fields use six custom-string slots; any overflow is explicitly reported and matched without extraction. Invalid IP/numeric samples are kept as custom strings. Quoting escapes, nested JSON/XML, multiline events and arbitrary delimiters require manual refinement; unsupported samples may be rejected when coverage validation fails.

One structure produces a main regex and tokens. Multiple structures produce a RawMessage token and ordered alternative patterns in a default submessage, with fields, types and formats. Message-ID routing is not inferred. Recognized textual severity labels receive conventional mappings with a review notice; numeric scales are not guessed. Extra processor dependencies and timestamp formats cannot be reliably inferred from examples and must be configured manually. This is a complete testable draft, not a certified production parser.

Limits: 250,000 pasted characters, 8,000 characters per event, 100 whitespace-separated fields per event and 30 structures. Validation is bounded and cancelled when samples change or the dialog closes. Every provided sample must match before a proposal can be applied. Matching samples alone does not prove field semantics or generalization.

## Event mapping snippets

Open **Event mapping…** after entering Message and the working Regex. Each existing mapping row and the draft value show the referenced token's captured text. Values come from the first match; repeated groups use their last capture. Empty captures, missing token captures and no-match errors are distinguished. Hover to see up to 2,000 characters. The visible snippet is capped at 120 characters.

Changing the mapping expression refreshes the snippet. String constants are displayed; other ArcSight expressions are explicitly marked as not evaluated. These are raw main-token previews, not the final event after submessage overrides, conversions or extra processors.

## Regex element navigation

Press F5, open **Regex elements**, then use **Element ▶ / ◀ Element**, select a list row or click inside the regex while that tab is selected. The application selects the corresponding regex source and highlights matching spans in Message. Elements include individual literals, escapes, character classes, quantified atoms, groups and assertions.

Repeated atoms may highlight multiple spans. Assertions and empty matches have zero-length positions and therefore no colored characters. Skipped alternatives have no capture. Source-order navigation describes the final first match; it is not execution-order stepping or internal backtracking. If there is no complete match, final captures are unavailable; the app does not falsely identify a first failing atom.

The bounded instrumentation supports ordinary groups, noncapturing groups, atomic groups, lookarounds, classes, escapes, quantifiers and alternatives. Backreferences, numeric escapes, named/balancing groups, conditionals, inline flags/comments and nested/subtracted character classes are explicitly rejected in element preview; normal capture debugging remains available. Limits: 8,000 regex characters, 512 elements, 64 nested groups and 5,000 displayed spans. Diagnostics explains unsupported syntax and timeouts.

## Code map

- ParserCore/ParserSuggester.cs: structural clustering, regex/token/mapping proposal and coverage validation.
- ParserCore/RegexElementDebugger.cs: conservative regex instrumentation and source-to-message spans.
- ParserDesigner.CrossPlatform/SuggestParserDialog.cs: isolated proposal, cancellation, validation and explicit apply.
- ParserDesigner.CrossPlatform/MappingDialog.cs: raw token and constant snippets.
- ParserDesigner.CrossPlatform/MainWindow.cs: element navigation and proposal integration.

All processing uses the existing local .NET engine. Validate the final configuration in ArcSight's Java-based connector.
