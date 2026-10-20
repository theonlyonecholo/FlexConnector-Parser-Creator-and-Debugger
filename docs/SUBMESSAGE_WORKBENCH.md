# Submessage workbench

Open **Submessages** to edit definitions and ordered pattern alternatives.
The main regex extracts two independent values:

- **MESSAGE ID INPUT** shows the raw value of `submessage.messageid.token`.
  Its label distinguishes no configured token, an undeclared token, failed
  extraction and an empty capture. An invalid submessage regex does not hide
  an independently extracted ID.
- **SUBMESSAGE INPUT** shows `submessage.token`, the body used by the selected
  pattern. Positions and highlights are relative to this body.

Edit the full event in **Original log**. Automatic preview refreshes extraction,
routing and highlights after a typing pause; F5 refreshes manually.
Both previews allow text selection and copying. Version 2.7.2 restores enough
height for the ID editor's caret scrolling, fixing a click-triggered stack
overflow reproduced on Linux/WSLg.
The selected definition's **Message ID** is its configured routing value, not
the extracted value above. **Actual route** identifies the definition/pattern
chosen by routing. An independent preview can match without winning routing.

## Finding a definition

Use **Search submessages…** in the left panel. Search is case-insensitive and
covers message IDs, definition/pattern indices, regexes and mapping values.
Matching patterns retain their parent definition for context. Definition matches
show all their patterns. Original indices and processing order do not change.
Draft edits are stored before filtering or switching; clearing the search
restores the list. Apply still exports every definition, not just visible ones.

## Choosing event fields

In **Fields / types**, place the caret in the comma-separated `fields` editor
and click **Choose ArcSight field…**.

The searchable picker uses the same documented field catalog and local custom
definitions as event mapping. Select a field to inspect its type, length,
restrictions and source. **Manage fields…** opens the existing persistent
catalog editor.

**Use field** replaces the comma-separated entry containing the caret.
At the end of a nonempty list it appends a field. An empty entry is filled
without changing neighboring entries. One undo reverses the insertion.

Field order must correspond to capture order or explicit mappings. The picker
does not rewrite `mappings`, `types` or `formats`; review their alignment after
adding fields. Defining local metadata does not extend the vendor's event schema.

## URL highlighting

Log and regex editors treat URLs and email addresses as plain text. Automatic
clickable-link generation is disabled so capture coloring and element selection
can split a URL into protocol, hostname, path or any other matched segments.
Text, matching behavior and copied values remain unchanged.

## Documentation checked

The user-supplied `flexconn_devguide_2023.pdf` identifies itself as
**ArcSight FlexConnectors 8.4.3, October 2023**. Printed pages 156–158 describe
the ID/body token distinction, ordered patterns and positional event fields,
including explicit types/formats. The PDF is not bundled in this repository.
The existing field catalog keeps its later CE 24.3 metadata.
