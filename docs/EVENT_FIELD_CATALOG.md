# Event field catalog

Event mapping's **Field catalog** displays the field name, type and documented
length. Selecting an entry shows its numeric range or other restrictions,
documentation source/version and whether it is a local definition. You can also
type a field key directly to see its metadata.

The bundled metadata follows OpenText's **FlexConnector Developer's Guide
CE 24.3 (July 2024), pages 184–189**, checked on 2026-10-20:
[official PDF](https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-25.1/pdfdoc/flexconn_devguide/flexconn_devguide.pdf).
The download is hosted under SmartConnectors 25.1; the document itself identifies
its version as CE 24.3.

Lengths and ranges are reference information, not automatic truncation or value
conversion. `n/a` is distinct from a missing length specification. The source
table does not define a universal byte/character counting rule, so the UI labels
the value as “Documented length”. Deployment behavior depends on the target
connector and destination. The guide's ambiguous `bytesOut` upper bound is
explicitly identified rather than silently corrected. Custom strings 1–6 use
the 4000 length documented in CE 24.3; their label fields use 1023.

Choose **Manage fields…** to:

- Add a field with **New field** and **Save definition**.
- Edit a built-in entry, saving a local override.
- Define a type name, length, range/limitations and documentation reference.
- Remove a custom entry, or restore a built-in with **Remove override / custom field**.

Field keys use `event.fieldName` syntax. Existing keys are read-only in the
manager; create another entry to use another key. Type names remain editable so
future vendor types can be described. Save before changing selection or closing:
unsaved form edits are discarded. Catalog edits save immediately and independently
of applying/cancelling the parser mapping dialog. Existing assignments are never
deleted by removing catalog metadata.

Local definitions are saved in `ArcSightParserDesigner/event-fields.json` under
the user's local application-data directory (typically `%LOCALAPPDATA%` on
Windows or `~/.local/share` on Linux). The manager shows the exact path.
Only overrides and additions are persisted; vendor defaults remain recoverable.
The file survives application upgrades and can be backed up separately. Invalid
files are reported without being silently overwritten. No network request occurs
when loading or editing the catalog.

The mapping dialog's advisory token-type checks use the effective local catalog.
Defining metadata does **not** extend the ArcSight event schema, implement a new
converter, or prove the target connector accepts that field. Configure supported
custom/additional-data facilities in the target product as appropriate. Unknown
fields can still be written as mappings, with advisory diagnostics.
