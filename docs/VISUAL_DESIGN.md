# Visual design

## Implemented in 2.7.1

The application mark combines brackets with three log lines on a navy tile.
It is an original project graphic, not an ArcSight or OpenText logo.

Native windows use the bundled PNG and the Windows executable includes a
multi-resolution ICO (16–256 pixels). Desktop environments control whether
window icons are displayed. The Linux executable does not install a desktop
launcher automatically.

Action buttons use original vector paths, with a consistent stroke and color.
Visible labels are retained; icons do not replace accessible button names.
The shared button helper covers the main window, submessages, mappings, testing,
catalogs and help. Buttons still wrap at narrower widths.
No icon fonts, external asset services or network requests are used.

Assets: `csharp/ParserDesigner.CrossPlatform/Assets/app-icon.svg`, `.png`, `.ico`.
Regeneration: `python scripts/generate-app-icon.py` with Pillow installed.
Runtime builds use the committed assets and do not require Python or Pillow.
Action vector paths live in `Icons.cs`.

## Suggested next changes — not implemented

1. **Group the main actions.** Use File, Parser and Testing groups. Keep Test/Debug
   prominent and put infrequent actions in an overflow menu at small widths.
2. **Resizable editor panes.** Let users adjust the message, regex and configuration
   areas; remember sizes and restore a useful default.
3. **Structured event preview.** Present fields as a searchable table with
   value, type and processing stage, plus expandable before/after history.
   Retain JSON as an export/detail view.
4. **Clear status badges.** Combine color with words and symbols for Match,
   Partial, Error and Incomplete. A legend should explain that partial highlighting
   is an estimate, not proof of the first failing character.
5. **Comfortable/compact density and dark theme.** Use shared theme resources
   for editor backgrounds, controls, icons and capture colors. Check contrast
   and focus indicators across the whole application before exposing a toggle.
6. **A quieter submessage sidebar.** Group patterns below collapsible definitions,
   show counts and route badges, and move the long usage guidance into Help.

Recommended order: action grouping and resizable panes first, then structured
event preview. These improve everyday editing before adding another theme.
