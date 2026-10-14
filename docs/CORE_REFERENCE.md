# Core module reference

All paths below are relative to `csharp/ParserCore`. Values passed between models
are decoded strings unless explicitly described as complete properties source.

## PropertiesDocument.cs

- Constructor: parses logical lines, continuations, comments, separators and escapes.
- `Values` / `Get`: effective values; duplicate keys use the last definition.
- `Decode` / `Escape`: Java properties decoding and ASCII-safe encoding.
- `Set(key, value)`: replaces the existing key in place, removes duplicate definitions,
  appends missing keys, or deletes the key for a null value.
- `RemoveWhere`: removes only keys accepted by the predicate.
- `JoinSources`: adds the required boundary, including after an unfinished continuation.
- `Load` / `Save`: Latin-1/BOM-aware reading and ASCII/Unicode-escaped writing through
  a temporary destination-directory file.

Untouched raw blocks and comments are retained. This is not a byte-for-byte promise
for non-ASCII export. Invalid Unicode escapes produce `FormatException`; I/O failures
are handled by the owning form. Never normalize the whole document just to change
one property.

## RegexDebugger.cs

`Analyze` accepts a decoded expression, input, .NET flags, full-event/trim switches,
timeout and cancellation. `DebugResult` contains match spans, `DebugRow` records,
status, elapsed time and optional syntax diagnostics.

A row identifies match number, group number/name, repeated capture number, success,
original input position, length and value. Group zero is the whole match. Missing
groups have position -1; zero-length captures are valid but not paintable.

Limits: 250,000 input characters, 16,000 expression characters, 1,000 matches,
5,000 rows and a captured-value character budget. The approximate total execution
budget is two seconds plus the currently executing bounded operation. Syntax errors
and regex timeouts become diagnostics; cancellation propagates.

## RegexGroupMap.cs

`Build` returns capturing-group source regions. It handles escapes, character
classes/subtraction, comments, named groups and scoped inline options. Named-group
numbers come from the engine. `At` chooses the narrowest enclosing region.
Invalid, conditional or balancing expressions return no source map. Capture
evaluation remains the debugger's responsibility.

## ParserEvaluator.cs

The constructor validates/compiles an immutable source snapshot. `Evaluate` first
matches the entire event, extracts configured token groups, routes the body by
message ID, then accepts the first matching pattern in order.

`ParseState` distinguishes matched, main mismatch, missing route, submessage
mismatch and error. `ParserResult` exposes message ID, route, description, capture
details, attempts and the selected numeric submessage/pattern indices. With
`details=false`, summary results retain the route but omit capture/attempt lists.

Unknown IDs may use a definition without `messageid`. A known ID whose patterns
fail never falls back to default. `ParserCapture.Group` identifies the submessage
group for editor navigation; mapping names are display labels, not evaluated fields.
Repeated submessage groups expose their last value, unlike debugger capture history.

## TokenGenerator.cs

`Draft` reads engine group numbers, rejects gaps and proposes `TokenDefinition`
records. Names and metadata are retained by index; new names avoid collisions.
`Apply` checks count, contiguous indices, required values and unique names, then
writes the regex and token declarations and removes obsolete token indices.
It does not rename references in mappings or submessages.

## TokenFormats.cs

`Types`, `Suggestions`, `Symbols`, `IsDateTime` and `Help` provide the type catalog
and format-builder content. These are templates based on the guide, not date parsing
or conversion APIs. UI-facing help strings remain Czech. Date/time symbols use
ArcSight/Java conventions, not .NET format strings.

## SubmessageConfiguration.cs

`Load` builds an editable model containing routing token names, trim behavior and
ordered `SubmessageDefinition` entries. Null `MessageId` means default. Unknown
definition and pattern options are kept in dictionaries.

`Apply` rewrites indexed definitions, recomputes counts/indices and validates the
result with `ParserEvaluator`. It retains unrelated configuration and rejects
definitions with no pattern. Limits are 1,000 definitions and 5,000 total patterns.
`NewPattern` creates a broad `(.*)` / `event.message` starting point.

## EventMapping.cs

- `Fields`: catalog of event field types; custom fields remain supported.
- `Load` / `IsDirectField`: select direct `event.name`-style assignments only.
- `Tokens`: declared token names and types.
- `Constant`: builds a quoted `__stringConstant` expression.
- `Apply`: rejects invalid keys, empty expressions and duplicate targets; replaces
  direct assignments while preserving submessage mappings and other properties.
- `Warnings`: advisory missing-reference/type warnings, missing `event.name`, unknown
  fields and requestUrl/component conflicts.

Expressions are stored unchanged after properties escaping. No expression evaluator
or return-type inference is provided. Warnings must not silently delete assignments.

## AdvancedConfiguration.cs

`ValidateSeverity`, `SeverityMatches` and `PreviewSeverity` handle exact values,
comma-separated lists and inclusive numeric ranges. Overlapping rules are reported
as ambiguous. Preview supports a direct severity token from the first regex match.

`ValidateProcessors` checks counts, contiguous indices, known types, required filenames
and recognized booleans. `ProcessorTemplate` generates editable starting blocks.
`Apply` updates severity and processor properties while retaining other content.
Linked files are neither located nor executed by this module.

## LogFile.cs

`Read` returns numbered `LogEntry` records, including blank lines. It accepts UTF-8
with BOM detection or Latin-1, checks cancellation during reading, and rejects
files over 32 MB or 50,000 lines. Each physical line is a separate event.

## Error handling convention

Draft validation uses exceptions for invalid configuration; forms catch expected
format/argument/I/O errors and present them. Runtime mismatches are result states,
not exceptional control flow. Do not catch cancellation as an ordinary parse error.
The exact exception contract of a new API should be documented alongside its tests.
