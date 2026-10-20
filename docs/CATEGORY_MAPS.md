# Category maps and editable vocabulary

Open **Category maps** from the main window or log workbench. Save a new parser
first, so relative CSV references have a stable location. Map creation is offline.
Use **Event mapping** to assign your ID token to `event.deviceEventClassId`.
Token search matches both names and raw sample values; the dropdown displays
a short snippet. Existing mapping rows retain their own snippet previews.

## Create and edit

1. Choose **Extra processor** or **Native categorizer**.
2. Enter an event class ID and category paths. Pick starter values from the
   dictionary or enter a path directly. Empty category cells are permitted.
3. Use **Add / update row**. Select an existing row to edit it; use **Remove row**
   to delete it. Search matches IDs and category text.
4. **Manage dictionary** lets you add, edit and delete shared vocabulary entries.
   Save the dictionary explicitly. It persists in the application's local-data
   directory as `ArcSightParserDesigner/categories.json`, independently of CSVs.
   Changing the dictionary does not rewrite existing map rows.
5. **Save map and apply** writes UTF-8 CSV and returns parser changes to the main
   editor. Save the parser there as well. **Open CSV** reopens a compatible map.

The editor uses `event.deviceEventClassId` as its getter and six setters:

| Category | Meaning |
| --- | --- |
| Object | What the activity concerns |
| Behavior | The action performed |
| Technique | The method used |
| Device group | The event-producing functional group |
| Significance | Security significance, distinct from severity |
| Outcome | Result of the activity |

Starter values are illustrative, not an exhaustive vendor taxonomy. Custom paths
are editable strings; the application cannot confirm they exist in your target
ArcSight content/AUP. The UI enforces a local 1,023-character path limit, a leading
slash and no control characters. This is not validation against an ESM schema.

## Two deployment modes

**Extra processor:** save inside the parser directory, outside the reserved
`categorizer/` folder. The application adds a `type=map` processor, or updates an
existing map processor referencing the same file. Other processors retain their
order. Created maps use UTF-8, case-insensitive getters, trimming and overwrite.
Review overwrite behavior when earlier mappings already set category fields.
Copy the CSV under `user/agent/aup/fcp/`, retaining its configured relative path.

**Native categorizer:** enter the exact final device vendor and product values.
The application saves `categorizer/<normalized-vendor>/<normalized-product>.csv`
beside the parser. The local preview automatically uses this directory after
supported parser/processor stages. Normalization lowercases ASCII names and
replaces punctuation with underscores. Set `event.deviceVendor` and
`event.deviceProduct` in the parser; the export form does not overwrite them.
Native categorization skips events with existing category values. A missing
vendor/product or referenced file makes the local preview incomplete.

The `categorizer/` directory is an **application preview convention**, not an
ArcSight parser property. Exported native CSVs must be deployed under the
connector's categorizer directory. The CE 24.3 guide, distributed on the 25.1
documentation site, specifies `user/agent/aup/acp/categorizer/current/`.
The older 8.4 HTML appendix gives `user/agent/acp/categorizer/current/`.
Use the layout documented for your installed connector. The guide also describes
reloading custom categorizations through the Console.
[Official developer guide, Map Files, pp. 212–214](https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-25.1/pdfdoc/flexconn_devguide/flexconn_devguide.pdf),
[8.4 categorization appendix](https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-8.4/flexconn_devguide/Content/convertFlex/Appendix_FlexConnectors_and_Categorization.htm).

## Test a log file

The log workbench evaluates event processing as well as regex routing. Each log
shows whether the local event preview completed and whether category values were
produced. The detail shows map row assignments, unmatched maps, category values
and diagnostics. Filters include incomplete processing and missing category
values. A valid regex match does not mean the event was categorized; an unmatched
map can be a complete preview with no categories.

After editing a map in the workbench, all logs are tested again. For external
CSV edits, press **Test all**. Keep input maps unchanged during a test run.
The selected-log detail reevaluates current files, so external changes can make
it differ from the last batch summary until you rerun the test.

Green/red highlighting continues to describe regex stages. Category results
appear as text because categorization uses event fields rather than a unique
substring of the source log. Use **Event preview / tests** for the full field
trace and expected-field assertions.

## Scope

The structured editor accepts exact-ID maps with precisely the standard seven
columns. It rejects other layouts rather than discarding them. The existing
extra-processor preview supports more general exact getter/setter CSVs, but not
regex/range/expression headers. Duplicate IDs are rejected case-insensitively.
Editor limits: 9,999 data rows, 4 MB CSV. No remote schema lookup or telemetry.
The local evaluator remains a supported subset, not a SmartConnector runtime.
Unimplemented operations and earlier processing failures remain explicit.

See the runnable `examples/categorization/` sample and
[event processing limitations](EVENT_PIPELINE.md).
