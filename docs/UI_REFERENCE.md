# Desktop module reference

All paths refer to `csharp/ParserDesigner`. UI text is English. Implementation
comments and identifiers are English. Forms are internal implementation details;
the tests intentionally inspect selected controls through reflection.

## Startup and main session

| File | Ownership |
| --- | --- |
| `Program.cs` | STA entry point, WinForms initialization and the main message loop. |
| `MainForm.cs` | Shared fields, initial sample, layout, control wiring and shutdown. |
| `MainForm.Toolbar.cs` | `CreateToolbar`: grouped File/Parser/Testing commands and frequent actions. |
| `MainForm.Configuration.cs` | Open/save, dirty state, regex synchronization, dialog integration and guarded UI actions. |
| `MainForm.Analysis.cs` | Debounce, cancellation, revision checks, worker serialization, debug rows and reports. |
| `MainForm.Highlighting.cs` | Capture navigation, source-group selection, colors and native caret/scroll preservation. |

These files form **one partial class**, not separate services. This preserves control
and lifecycle ownership while making responsibilities navigable. Shared state is
deliberate: do not create another independent copy of the current source or result
in a partial file.

`loading` suppresses dirty/preview reactions to initialization and file loading.
`populating` suppresses selection reactions while rebuilding grids. `coloring`
prevents formatting feedback loops. `revision` invalidates outdated async work.

## BatchForm.cs

Owns the log list, summary result array, filtered index list and selected detailed
result. The virtual grid requests values on demand instead of allocating full cells
for every log. Visible row indices are not source line numbers: translate through
the `visible` list before reading or updating a log.

`RunAsync` captures the evaluator, iterates logs in background, reports progress and
restores selection. `SelectLogAsync` serializes detail evaluation and checks a detail
version before publishing. `ApplyParserAsync` replaces the snapshot and reruns the
test. `ConfigurationSource` returns the edited source to the owner when the form
closes. Separate cancellation sources control whole-file and selected-detail work.

## Configuration dialogs

| File | Draft behavior and result |
| --- | --- |
| `TokenDialog.cs` | Reviews `TokenGenerator.Draft`, edits names/types/formats, returns `ResultSource`. Existing metadata follows group index. |
| `TokenFormatDialog.cs` | Builds a decoded format using presets, symbols and editable text; returns `ResultFormat`. |
| `SubmessageDialog.cs` | Owns an editable submessage model. Commits the previous tree node before switching; supports add/remove/reorder and draft testing. Returns `ResultSource`. |
| `EventMappingDialog.cs` | Edits a table of direct assignments; helper controls add/update a target. Pending helper input must be applied or cleared. Diagnostics are advisory except structural validation. |
| `AdvancedDialog.cs` | Edits severity rules and processor source blocks; templates append to the draft. |
| `ParserSourceDialog.cs` | Edits a complete source string for batch retesting; validates with `ParserEvaluator` before accepting. |

Dialogs return data only on acceptance. They do not save to disk. The main document
is responsible for the final save and its confirmation flow. A cancelled dialog
must not mutate the owner's configuration.

`TokenDialog` also captures the current Message and preview options. After showing,
it evaluates a bounded preview in the background and fills the read-only snippet
column from the first match's last capture per group. Closing cancels the preview;
late results are ignored. Snippets and their tooltips are never serialized.

The submessage editor clears text history when switching tree nodes to prevent
undo from applying another pattern's text to the currently selected pattern.
Its async group preview uses a version and semaphore to reject stale results.

## Shared presentation modules

### Theme.cs

Applies common colors, fonts, button and grid styling recursively. At the form
boundary it installs contextual help once. Applying the theme to a help window
must not install another help footer recursively.

### TokenColors.cs

Provides the deterministic token/group palette used by message highlighting and
grid legends. The same group index must map to the same color in both locations.

### EditHistory.cs

Contains `EditHistory`, `UndoTextBox` and `UndoRichTextBox`. The controls intercept
Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z and offer the same commands in a context menu.
`Begin`/`End` bracket native window-message transactions. `Changed` records one
minimal contiguous text replacement; `CreateChange` computes common prefix/suffix
boundaries. `Restore` suppresses recursive history recording while still raising
normal control changes. `ResetHistory` begins a new document/session boundary.

Capacity is 100 changes. New edits discard redo; formatting-only changes do not
count. The last text snapshot is retained for diffing, with older states stored as
differences. Read-only controls must not apply undo commands.

### HelpTopics.cs

Stores the bundled English topic catalog as `HelpTopic` records. It documents product
behavior and ArcSight concepts with source page references. Keep limitations in
sync with the implementation; do not describe an unsupported conversion as executed.

### HelpUi.cs

Installs tooltips, accessible descriptions, F1 routing and a status-strip entry.
State is keyed by form through a weak table. `Describe` associates a control with
text and a topic after it has been attached to its form. Tooltips are disposed when
the form closes. Grid header descriptions explain column semantics.

### HelpWindow.cs

Shows topics and searches both titles and full content. It preserves selection
when possible, displays an explicit empty-search result and supports Escape to
close. It has no dependency on the PDF file at runtime.

## Layout and navigation rules

- Use grouped commands instead of adding another wide main-toolbar button.
- Wrapping flow panels need content-sized table rows, not a fixed height that clips
  a second row. The main toolbar supports overflow as an additional fallback.
- Respect supported minimum sizes; larger default screenshots alone are insufficient.
- Keep Message/Regex editable while viewing results. Preserve selection and scroll
  during formatting and never steal focus from the user to paint highlights.
- Keep labels, keyboard access, F1 routing and accessible descriptions aligned.

See [development recipes](DEVELOPMENT.md) before adding a new editor or preview.
