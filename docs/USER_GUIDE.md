# User guide — ArcSight Parser Designer 1.1.0

The C# application runs on Windows with .NET 10 Windows Desktop Runtime.
Start `app/ArcSight.ParserDesigner.exe` from the prepared distribution.
All application text and offline help are English. Native Windows dialogs use the system language.

Hover over controls for hints; press **F1** for contextual, searchable offline help.
The following reference covers all eleven help topics.

## Getting started: from log to event

```text
A parser describes how to turn original log text into a structured ArcSight event. The regex recognizes text, tokens name captured values, and mappings assign them to event.* fields.

Processing overview
1. The connector receives a record. Input handling and multiline assembly depend on the connector type.
2. The main regex divides the message into capturing groups.
3. token[n].name/type/format specifies each value's name, data type and optional format.
4. Optional submessages distinguish body variants using a message ID and additional regex patterns.
5. event.* mappings assign values to target fields; operations can convert or supplement them.
6. Severity mapping normalizes severity. Extra processors can invoke additional parsers or mappings.
This explains the configuration components, not every internal connector stage.

Your first parser
Open a .properties file or edit the initial example. Paste a log into Message and enter a decoded expression in Regex. Press F5, inspect groups and choose Tokens from regex. Configure types and event mappings. Try several logs with Test log file, then Save.

Example
Message: status=404 message=Resource missing
Regex: status=(\d+) message=(.*)
Group 1 contains 404; group 2 contains Resource missing.
Map Status to event.deviceSeverity and Message to event.message.

A successful local test confirms regex matches, not the complete correctness of the ArcSight event. Verify the parser in the target connector before deployment.
Reference: FlexConn_DevGuide.pdf, pp. 69–76.
```

## Regex, Message and the debugger

```text
Message is the original text being tested. Regex is the pattern used to recognize it. Long expressions wrap visually without inserting newlines into their content.

Basic syntax
\d = digit, \s = whitespace, \S = non-whitespace character, . = any character except a newline in the usual mode.
+ means one or more, * zero or more, ? optional, {2} exactly twice. [A-Z] selects a character from a range; A|B selects an alternative. ^ and $ mark line/input boundaries depending on the mode.
(...) captures a value and adds a group. (?:...) groups an expression without a token. (.*) is very broad; prefer a more specific pattern for a known field.

Click inside a capturing group to highlight its captures in blue in Message. For nested groups, the innermost capturing group is selected. An unmatched group differs from an empty capture. Empty captures show a position but have no text to highlight. Click navigation does not support conditional or balancing groups.

Automatic: refresh after a typing pause. When disabled, press F5.
IgnoreCase (i): ignore letter case.
Multiline (m): change ^ and $ to work with individual lines; this does not combine logs.
Singleline (s): allow the dot to match newlines.
Entire message: require the regex to cover the whole input. Otherwise the preview can find multiple partial matches. File testing always requires the entire message.
Trim spaces/tabs: remove spaces and tabs from both ends for the preview; capture positions still refer to the original text.
Timeout ms: limit individual regex operations so a complex expression cannot block the preview indefinitely.
Stop: request cancellation; a running regex finishes or reaches its timeout.

Debug table
Match = match sequence number. Group 0 = entire match; Group 1 = token[0]. Capture = sequence number of repeated captures within a group. Success = whether the group captured a value. Position = zero-based UTF-16 index; Length = number of UTF-16 units. An unmatched group has position -1. Value = captured text. Click a row to highlight that capture; arrows navigate captures, not backtracking steps.

The i/m/s, Entire message and Trim switches are preview settings. Configure flags and trim.message in the properties file for the actual parser.
```

## Properties configuration and saving

```text
The configuration is a text file of key=value properties. Keys are case-sensitive. Comments begin with # or !. The last definition of a duplicate key wins. A trailing backslash can continue a value on the next line.

Two representations of a regex
In the Regex field: status=(\d+) message=(.*)
In .properties: regex=status=(\\d+) message=(.*)
Properties has its own escaping, so backslashes are doubled. Regex → Configuration escapes automatically; Configuration → Regex decodes the value.

Minimal configuration example
regex=status=(\\d+) message=(.*)
token.count=2
token[0].name=Status
token[0].type=String
token[1].name=Message
token[1].type=String
event.deviceSeverity=Status
event.message=Message
severity.map.high.if.deviceSeverity=400..599

Open parser loads a file and its main regex. Save writes the current configuration; Save as chooses another file. If the Regex field differs, the application offers to write it into the configuration. Applying a dialog does not save to disk.
Configuration → Regex replaces the working expression with the configured value. Regex → Configuration updates the regex property, not the tokens. After changing groups, check token.count, names and dependent references.

Properties files are read as Latin-1 or using their BOM. ASCII export with Unicode escapes preserves international characters. This is separate from the log file encoding setting.

Ctrl+Z: undo up to 100 text edits in the active field. Ctrl+Y / Ctrl+Shift+Z: redo. A new edit discards the redo branch. History is not saved to disk. Opening another parser resets it. Generated changes can be undone in the main configuration field. History does not cover structural tree or table operations.
Reference: guide, pp. 69–71; history and saving details describe this application.
```

## Tokens and data types

```text
A token is a named value from a capturing group. The first group corresponds to token[0], the second to token[1]; token.count specifies the count. The name is used in mapping references. The type should match the target event field.

Tokens from regex
The generator creates one row per capturing group. Group 0 and (?:...) do not create tokens. Existing metadata is reused by index; check name meanings after reordering groups. Names must be nonempty and unique. Apply also writes the main regex and removes surplus tokens. It does not rename references in mappings or submessages.
Message snippet shows raw token text from the first match in the current message. For repeated groups, it shows the last capture. The preview uses the main debugger settings, distinguishes empty and unmatched tokens, and does not convert types. Hover over a cell for a longer snippet. Snippets are not saved into the parser.

Types
String: text, such as a user name or message.
Integer: whole number from -2147483648 to 2147483647, such as a port.
Long: whole number from -9223372036854775808 to 9223372036854775807.
Double: decimal number; the guide documents it for ESM domain field sets (p. 204).
IPAddress: IPv4; IPv6 with ipv6.aware=true. Check the configuration before switching a parser to IPv6.
IPv6Address: the guide specifies 16 bytes represented as 32 hexadecimal characters.
MacAddress: MAC address, e.g. 00:06:3E:22:51:B9, also with hyphens.
Date: date; suggested default MM/dd/yyyy.
Time: time of day; suggested default HH:mm:ss.
TimeStamp: date and/or time; for example yyyy-MM-dd HH:mm:ss.
RegexToken: further extraction by substitution, such as s/From: (.*)/$1/.

The Type column provides a list. Date, Time, TimeStamp and RegexToken supply a suggestion when Format is empty. Existing formats are preserved: check them after changing type. Unknown types from the original file remain in the list.
Format builder opens the helper for the selected row. You can also enter a format directly in the table. An empty format removes token[n].format on export.
The local regex debugger shows raw captures; it does not perform these type conversions.
Reference: guide, pp. 71, 196–197 and 204.
```

## Date, time and RegexToken formats

```text
A format tells the connector how to read a token value. It is not a regex. It uses ArcSight/Java symbols, not .NET formatting.

Format builder
Choose a template and edit Format. Clicking a symbol inserts it at the cursor or replaces selected text. Apply format returns the value to the token row. Clear format empties the field; Cancel discards the draft. Ctrl+Z undoes text edits.

Common symbols
yyyy year, MM numeric month, MMM abbreviated month name, dd day of month.
HH hour 0–23, hh hour 1–12 with a for AM/PM, kk hour 1–24, KK hour 0–11.
mm minutes, ss seconds, SSS milliseconds. Take care with MM versus mm!
EEE day of week, G era, w week of year, W week of month, D day of year, F day of week in month.
z time zone name, Z numeric RFC 822 time zone (e.g. +0200).
Enclose literal text in single quotes: 'T' or 'at'. Two single quotes represent one literal single quote.

Example values → formats
2026-10-14 09:30:05 → yyyy-MM-dd HH:mm:ss
2026-10-14T09:30:05+0200 → yyyy-MM-dd'T'HH:mm:ssZ
09:30:05.123 → HH:mm:ss.SSS
Oct 14 09:30:05 → MMM dd HH:mm:ss
Syslog without a year requires supplying one through mapping, e.g. __useCurrentYear. Verify the month-name language and time zone in the connector.

RegexToken
s/From: (.*)/$1/ extracts rajiv from From: rajiv. The part before the second slash is a regex; $1 in the replacement refers to its first group. This further processes the token value; it is not the main regex for the entire log.

The builder produces configuration text; it does not validate conversion of a particular value. Templates are starting points: adapt them to the actual log. Enter a decoded format; export handles properties escaping.
Reference: guide, pp. 138 and 196–197.
```

## Event mapping and operations

```text
Normalization maps different logs to common ArcSight fields. A token is an input variable; event.* is a target event field.

Direct mapping
event.sourceAddress=ClientIp
event.sourcePort=ClientPort
event.message=Message
event.deviceReceiptTime=Timestamp
The token type must match the target field type. IPAddress differs from String even when both look like IP address text.

Expressions and constants
event.deviceProduct=__stringConstant("MyProduct")
event.deviceReceiptTime=__useCurrentYear(Timestamp)
Operations beginning with __ can convert numbers, combine text, supply time components or select values. Parameters are token references or literals as required by the operation. See Appendix A of the guide for exact signatures.

Field meanings
source/destination identify the source and destination of the described event; device identifies the reporting device. deviceSeverity is the original severity or selected device status code. message contains message text. Understand the log before mapping addresses to sourceAddress.
requestUrl contains the full URL. Avoid assigning it together with derived URI components of the same event; the guide warns of a conflict.

Event mapping editor
Choose a target field to see its expected type. Token mode lists declared tokens, Constant creates __stringConstant, and Expression stores an entered operation. Add / update inserts the draft into the table or updates the same field. Add or clear any unfinished draft above the table before applying.
Double-click table cells to edit existing mappings. Remove mapping deletes the selected row. Apply to configuration writes changes into the main text; then Save. Cancel discards the draft. Direct event.* mappings are separate from submessage mappings and other properties, which the editor preserves.
The editor rejects duplicate fields and empty expressions. Diagnostics warn about unknown direct tokens, type mismatches against the field catalog, missing event.name, or requestUrl/URI conflicts. Warnings do not prevent saving because another parser component may supply values. Custom event fields can be entered manually.
The application does not execute general ArcSight operations or event mapping. Type checks cover direct tokens, not expression return types. A green regex match does not validate the resulting event.
Reference: guide, pp. 71–73, Appendix A and Appendix E from p. 198.
```

## Submessages: multiple log variants

```text
A submessage is part of the original log parsed by another pattern after the main regex. It is useful for logs with a common header and different body formats.

Example
Log: id=LOGIN body=user=alice result=success
Main regex in the editor: id=(\S+) body=(.*)
token[0].name=MessageID, token[1].name=Body (both String).
Submessage token = Body; Message ID token = MessageID.
The submessage with Message ID LOGIN uses regex user=(\S+) result=(\S+).
fields = event.sourceUserName,event.deviceAction
The first submessage group is $1 (alice), the second $2 (success). Numbering starts again at one.

Editor fields
Submessage token: the token containing the body, property submessage.token.
Message ID token: the token selecting a definition, submessage.messageid.token.
Trim submessage: remove leading and trailing spaces/tabs before testing the body.
Message ID: the exact value this definition handles.
Default for unknown IDs: a definition without a messageid property. Used for undefined IDs, not when all patterns for a known ID fail.
regex: decoded pattern for the entire submessage body.
fields: comma-separated target event fields in capturing group order.
types: explicit types in the same order; otherwise the connector may infer types from target fields.
formats: formats in the same order; null marks an item without a format.
mappings: expressions for individual fields, e.g. __getProtocolName($1)|$2. The default separator is | because operations may contain commas.
mappings.delimiter: a custom mappings separator.
extramappings: additional assignments, e.g. event.name=__stringConstant("Unparsed event").
extramappings.delimiter: separator for additional assignments (default |).

Tree and order
+ Submessage adds a definition. + Pattern adds a variant to the selected definition. Remove deletes the selected pattern or entire definition. Earlier/Later changes the order. The first matching pattern wins; put specific variants before broad ones such as (.*). Indices and counts are recalculated on Apply.

Test log with these changes evaluates the draft against the entire selected log. Clicking inside a group highlights its field if this pattern was selected. Repeated groups show the last field value. Apply to configuration returns changes to the editor; then Save. Cancel discards the draft. Switching nodes resets the fields' text histories.
Local testing performs routing and regex matching; fields are labels. It does not execute types, formats, mappings or extramappings.
Reference: guide, pp. 136–146.
```

## Log file testing and debugging

```text
Test log file uses one parser for every line in a file. One physical line = one event, including empty lines. Multiline configuration does not combine lines here. Limits: 32 MB and 50,000 lines.

Workflow
1. Select an encoding and Open logs. UTF-8 recognizes a BOM; use Latin-1 only for files in that encoding.
2. Testing runs in the background. Test all repeats it; Stop leaves remaining rows untested.
3. Select a log or use the arrows. Filter the list to matches, problems or untested entries.
4. Details show the original text, colored tokens/fields, message ID and selected submessage[n].pattern[m]. The attempt list shows which patterns failed.
5. Edit submessage opens the selected or first attempted pattern. Add a variant for a different log. Edit parser opens the entire configuration. Applying changes automatically retests the file.
6. Changes return to the main configuration when you close the window. Use Save to write them to disk.

Results
Match: the main regex and any submessage match.
Main regex: mismatch: first fix the common header, separators or overall pattern.
Missing submessage: no definition for the message ID and no default.
Submessage: mismatch: the ID is known, but no body variant matches. A default does not fix this.
Error / timeout: a limit or processing error; inspect the details.
Not tested: the record has not yet been evaluated.

After a fix, check previously successful logs too. A broader pattern can change the selected variant. The selected line is retained if it remains in the active filter.
Testing uses the configuration, including trim.message and trim.submessage, rather than the main debugger's experimental switches.
```

## Severity: normalize device severity

```text
Devices may report different severity scales or only a status code. event.deviceSeverity carries the original value; severity.map.* maps it to low, medium, high or veryhigh.

In the dialog, first enter event.deviceSeverity, for example Status (a captured token name). Then fill in rules:
high: 400..599
medium: 300..399
low: 100..299
Lists can contain text or numbers, such as OPEN,CLOSE or 404,500. Ranges include both endpoints.

Resulting property:
severity.map.high.if.deviceSeverity=400..599
Status 404 maps to high. Empty rules are removed. Choose rules based on event meaning, not just the size of a number.

The preview supports a direct event.deviceSeverity reference to a token in the first match. General ArcSight expressions can be saved but are not evaluated in the preview. Text comparison is exact; overlapping rules are marked as ambiguous without assuming precedence. Check coverage of expected values.
Reference: guide, pp. 73–75.
```

## Extra processors: downstream parsers

```text
An extra processor passes a value to another configuration. For example, it can process JSON within event.message or supplement values from a map file. It differs from a submessage: it references a separate file and may use another parser type.

Choose a type and Add template. Supply the path and parameters. Indices must be consecutive: 0,1,2,... without gaps; the count is calculated on Apply. After removing a block, renumber subsequent indices.

Types
regex: another regular expression; delimited: delimiter-separated fields; json: JSON; xml: XML; keyvalue: key/value pairs; standardkeyvalue: key=value with comma-separated pairs; map: mapping file; ntsubparser: supported Windows application parsers.

Parameters
type: processor type. filename: reference to its configuration, not the parser content.
field: input event field, such as event.message.
flexagent: true selects parsers in flexagent; false selects fcp.
clearfieldafterparsing: whether to clear the input field after parsing.
overrideeventmappings: whether to overwrite existing event field mappings.
charencoding: character encoding.
conditionfield: field used for a condition; conditiontype: comparison method; conditionvalues: comma-separated values. Verify valid condition types for the particular processor.
casesensitive / Casesensitive: letter-case handling. Property names are case-sensitive; use the key required by the processor. The guide's table uses Casesensitive.
concatenatevalues: combine duplicate key values in key-value parsers instead of overwriting them.

Example
extraprocessor.count=1
extraprocessor[0].type=regex
extraprocessor[0].filename=vendor/detail
extraprocessor[0].field=event.message
extraprocessor[0].flexagent=true
extraprocessor[0].clearfieldafterparsing=false

The guide places regular configurations in user/agent/flexagent and map files in user/agent/fcp or user/agent/aup/fcp (aup takes precedence).
The application checks configuration structure, not file existence. It does not run linked parsers locally. Replace template paths with actual paths.
Reference: guide, pp. 75–76.
```

## Preview scope and connector validation

```text
The application performs properties parsing/writing, regex matching and capture extraction, capture positioning and coloring, submessage routing and first-pattern selection, log file testing and a limited severity preview.

It does not perform general ArcSight operations, full event mapping, token type conversions, token format processing, RegexToken substitutions, conditional mapping, linked extra processors or multiline event assembly during file testing.

The regex engine is .NET; ArcSight uses Java. Named groups, their numbering, some constructs and Unicode behavior may differ. A local match does not confirm compatibility of every construct.

Preview limits: 250,000 message characters, 16,000 regex characters, 1,000 matches and 5,000 capture rows. Total processing time is bounded, and each regex operation has a timeout. Truncated results are not a complete list of captures. Debug shows results, not internal backtracking.

Troubleshooting
1. Check the actual log, spaces, separators and encoding.
2. Distinguish the decoded regex from its escaped properties value.
3. Check token.count, group order, names and references.
4. For submessages, check the body, message ID, variant order and default.
5. Verify the resulting event's types, formats, operations, time zone, severity and linked files in the connector.

ArcSight reference: FlexConn_DevGuide.pdf, especially pp. 69–76, 131–146, 177–197 and 198–204. Button behavior and limits describe this C# application. Help works offline and searches topic content as well as titles.
```
