# Event preview and expected-event tests

Available from **Event preview / tests…** in the main window and log-file tester.
This is an offline preview of supported stages, not the proprietary ArcSight
runtime. The existing log-file result list still tests regex extraction and routing.

## Workflow

1. Load a parser and sample log, then open the preview.
2. Inspect **Configuration snapshot**. The window uses this snapshot; reopen it
   after changing the parser.
3. Choose a **Dependency root** matching your local connector configuration root.
   Relative `extraprocessor[n].filename` paths resolve below this directory.
4. Evaluate the event. The result contains final fields, ordered writes with
   previous/new values, diagnostics and dependency paths.
5. Edit or load **Expected tests (JSON)**, run tests and save them for reuse.

Try `examples/event-pipeline.sdkrfilereader.properties`, either line from its
`.log`, and the examples directory as dependency root. Load
`event-pipeline.expected.json` for two complete assertions.

**INCOMPLETE** means a failed parse, ambiguity, missing dependency, unsupported
operation or other unresolved processing step. Such a case cannot pass an
expected-event test. A complete local result only means the modeled stages were
evaluated; validate production parsers in the target connector.

## Compatibility inspection

The inspector recognizes common regex, delimited, key-value, JSON, XML and
database indicators in keys and filenames. Only an unambiguous regex parser is
evaluated. This is a heuristic, not an exhaustive parser-family registry.
Duplicate keys show their physical source lines and effective last value;
source text is preserved and the preview is marked incomplete.

## Modeled stages

The local order is main token conversion, main event/additional-data assignments,
global conditional maps, selected submessage assignments, selected-pattern
conditional maps, then numbered extra processors. This order is an inspectable
local model, not a certification of every connector version's execution order.

Conditional maps support token/field selectors, ordered value branches and a
default branch without `.values`. Submessage mappings support `$n`, reordered
expressions, declared types/formats and extra assignments.

Supported expressions:

- `__stringConstant`, `__integerConstant`, `__longConstant`
- `__concatenate`, `__oneOf`, `__oneOfInteger`, `__oneOfLong`
- `__safeToInteger`, `__safeToLong`
- `__toLowerCase`, `__toUpperCase`, `__stringTrim`
- `__parseMultipleTimeStamp`

Expressions can reference tokens, current fields and submessage captures.
Nested calls and quoted arguments are supported. Numeric fallback selects the
first nonempty value before parsing it. Safe numeric failures produce null.
Unknown operations remain unresolved; no arbitrary code is executed.

Token types: String, Integer, Long, IPAddress and a limited TimeStamp subset.
Timestamp formats supported:

```text
yyyy-MM-dd HH:mm:ss
yyyy-MM-dd HH:mm:ss.SSS
MM/dd/yyyy HH:mm:ss.SSS
EEE MMM dd HH:mm:ss yyyy
yyyy-MM-dd'T'HH:mm:ssXXX
yyyy-MM-dd'T'HH:mm:ss.SSSXXX
```

Timestamps become epoch milliseconds; timestamps without offsets use UTC.
Other configured time zones are explicitly unresolved. Target integer, long,
address and timestamp fields receive type checks. Schema lengths and destination
specific ranges are not validated by this feature.

## Map files

Explicit `map` extra processors support CSV headers such as:

```csv
event.destinationPort,set.event.applicationProtocol
80,http
22,ssh
```

All getters must match. Quoted cells, embedded newlines and doubled quotes are
supported. A map without getters can contain one unconditional row.
Options include `allowoverwrite`, `casesensitive`, `trimgettertokens`,
`trimsettertokens` and `charencoding`; corresponding CSV flags are supported.
Overwrite, case sensitivity and trimming default to true.
Existing non-null values are retained when overwrite is disabled.

Local default encoding is strict UTF-8, whereas the connector's default can
depend on its platform. ASCII and Latin-1 are also supported. Select an explicit
encoding for reproducible tests.

Range/regex/expression headers, other processor types and duplicate getter
tuples are unsupported. Duplicate tuples are rejected even with `EnfrcUniqID-`.
Unavailable or unsupported processing stops later processors rather than
pretending they ran. Files are limited to 4 MB, 10,000 rows and 128 columns.
Paths must resolve beneath the selected root; use trusted local dependencies.

## Expected-event JSON

```json
{
  "version": 1,
  "cases": [
    {
      "name": "HTTP event",
      "log": "user=alice action=allow port=80",
      "fields": {
        "event.sourceUserName": "alice",
        "event.destinationPort": "80",
        "event.message": "HTTP traffic"
      },
      "absentFields": ["event.sourceAddress"]
    }
  ]
}
```

All non-null expected values are strings, including numbers and epoch timestamps.
Null asserts a present field with a null value; `absentFields` asserts no field.
Only listed fields are compared. Optional `route` checks the exact route string
reported by the preview. At least one assertion is required per case.
Limits: version 1, 500 cases, 4 MB fixture and 250,000 characters per log.

PASS requires complete processing and matching assertions. FAIL means complete
processing but mismatching assertions. INCOMPLETE takes precedence over FAIL.
Input changes cancel pending work and invalidate previous output.

## Boundaries and sources

Severity processing, arbitrary operations, external scripts, automatic map
directory processing, multiline ingestion and the complete ArcSight execution
environment are not implemented here. Recognized unsupported stages are
reported, but the inspector is not an exhaustive validator of all possible keys.
Regex behavior remains .NET behavior, not Java engine equivalence.
All evaluation and dependency reads are local.

The implementation was checked against the public
[FlexConnector Developer's Guide, CE 24.3, July 2024](https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-25.1/pdfdoc/flexconn_devguide/flexconn_devguide.pdf)
(in particular map files and extra processors) and the official
[Sub Messages documentation](https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-8.4/flexconn_devguide/Content/convertFlex/Sub_Messages.htm).
The PDF's own version is CE 24.3 despite the hosting URL containing 25.1.
