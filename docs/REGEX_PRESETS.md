# Regex presets

Select literal text in either regex editor, open **Regex presets…**, choose a
saved preset and use **Check and replace selection**. For example, selecting
`192.0.2.1` and choosing IPv4 replaces that text with the saved expression.
The whole selection must match. Selection text is not interpreted as another
regex. Invalid presets, mismatches, timeouts and invalid resulting expressions
leave the editor unchanged. Testing one example does not prove equivalence for
other logs.

Use **New**, **Save preset**, and **Delete** to manage the library. Save form edits
before applying them. Switching presets or closing discards unsaved form edits.
Built-ins are editable and deletable too; deleted defaults do not reappear.
Use decoded regex syntax (a single backslash), not Java-properties escaping.

The shared library is `ArcSightParserDesigner/regex-presets.json` under
`Environment.SpecialFolder.LocalApplicationData`. Typical locations are
`%LOCALAPPDATA%\ArcSightParserDesigner` on Windows and
`~/.local/share/ArcSightParserDesigner` on Linux. The dialog shows the exact path.
The file lives outside the application release directory, survives restarts and
upgrades, and is not sent anywhere. Back it up to move the library to another
computer. Invalid files are reported rather than silently overwritten. Saves
replace the file via a temporary file. Limits: 200 entries, 16,000 characters per
regex, 4 MB per library; matching has a 250 ms timeout.

Replacement uses a noncapturing `(?:...)` wrapper to preserve alternation scope.
**Group selection** wraps the selected expression in capturing parentheses. Both edits
are single undo steps. Capturing groups inside custom presets and groups created
with the button change capture numbering: review token declarations and
submessage mappings. Preset management saves immediately; editor undo does not
undo library changes.

Built-ins:

| Preset | Scope |
|---|---|
| IPv4 | Four decimal octets in 0–255, without leading zeros |
| IPv6 | Full, compressed and IPv4-tail forms; no brackets, zone or CIDR suffix |
| FQDN | At least two ASCII labels, each up to 63 characters; optional final dot; no total-length or DNS-existence check |
| MAC | Six colon-separated hexadecimal octets |
| Unsigned integer | Decimal digits, without an upper numeric bound |
| UUID | Hexadecimal 8-4-4-4-12 shape; no version/variant restriction |

The IP expressions follow the address grammar in
[RFC 3986 §3.2.2](https://www.rfc-editor.org/rfc/rfc3986.html#section-3.2.2).
They are original preset expressions, not copied reference implementations.
The application tests with .NET regex. Final ArcSight compatibility and event
mapping must be checked in the target connector's Java runtime.
