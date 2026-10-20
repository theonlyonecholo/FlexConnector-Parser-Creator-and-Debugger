# Public parser review and proposed next steps

Reviewed 2026-10-20. This is a configuration review, not execution of the
proprietary SmartConnector runtime. Recommendations below are not implemented
unless explicitly noted.

## Example examined

[S3COPS Sysmon parser](https://github.com/S3COPS/ArcSight-Sysmon-FlexConnector/blob/master/fcp/winc/microsoft_windows_sysmon_operational/microsoft_windows_sysmon.sdkkeyvaluefilereader.properties)
is a public Windows Native Connector key-value parser. It separates keys using
`&&` and values using `=`, trims values, enables IPv6 awareness, assigns common
fields, and converts timestamps/numbers through ArcSight operations. It extracts
hashes, retains extra attributes in additional data, applies 27 conditional-map
branches using the event ID, and configures two map-file processors.
Consequently, matching a regex alone cannot reproduce its resulting event.
The file also contains repeated assignments to mapping index 0 within a later
event section: an opportunity for duplicate-key diagnostics, not proof of a
runtime defect.

For a regex-oriented comparison, the
[official PIX submessage example](https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-8.4/flexconn_devguide/Content/convertFlex/Sub_Messages.htm)
extracts a common header, routes the body by ID, tries ordered pattern
alternatives, and supports a default definition.

## Recommended changes to this application

| Priority | Proposal | Acceptance criterion |
|---|---|---|
| P0 | Detect parser family before evaluation | Key-value, JSON and other unsupported families receive an explicit compatibility report, not simply “missing main regex”. |
| P0 | Report duplicate property keys and suspicious index reuse | Show both source locations and the effective value; preserve original text until the user resolves the warning. |
| P0 | Separate regex success from event-processing completeness | A successful capture cannot imply that conversions, conditional mappings or external processors were evaluated. |
| P1 | Add an allowlisted offline operation evaluator | Start with constants, concatenation, fallback values and safe numeric/time conversion; unsupported operations remain explicitly unresolved. |
| P1 | Implement conditional-map preview and additional-data editing | Show the chosen branch, default branch, final field values and where each value came from. Preserve `additionaldata.*` keys. |
| P1 | Resolve map-file dependencies and overwrite order | List missing files, selected rows, before/after values and processor order without silently skipping stages. |
| P1 | Add target-version schema profiles | Validate resolved values against an explicit connector/destination profile, not one universal length table. |
| P1 | Build expected-event regression fixtures | Compare final fields as well as routes; include invalid numbers, empty/missing values, timestamps/time zones and IPv4/IPv6. |
| P2 | Offer an optional local Java regex comparison | Report .NET/Java behavior differences without claiming full connector emulation. |

The current release implements the editable local field catalog and fixes
caret-driven regex selection. The pipeline and compatibility proposals above
remain future work. No third-party parser code or connector binary is bundled
as a result of this review.
