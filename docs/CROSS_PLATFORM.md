# Cross-platform desktop edition — 2.2.1

This edition uses C# / .NET 10, Avalonia 12.1.4 and AvaloniaEdit 12.2.1.
It is a native desktop application, not a website or a Wine wrapper. The existing
WinForms edition remains available in its separate Windows solution.

## Ready-to-run packages

The prepared distribution has `app/linux-x64/` and `app/win-x64/`. Both include
the .NET runtime. Keep every file in the selected directory together; copying
only the launcher is insufficient. The application does not require administrator
access to edit files in a directory you own. Operating-system library installation
may require administrator access.

On Ubuntu, extract the Linux archive, open a terminal in its directory, then run:

```bash
chmod +x ArcSight.ParserDesigner.CrossPlatform
./ArcSight.ParserDesigner.CrossPlatform
```

Use an interactive Linux desktop with X11 or XWayland. The application currently
uses Avalonia's X11 backend, including under WSLg. Install its native dependencies:

```bash
sudo apt update
sudo apt install libx11-6 libice6 libsm6 libfontconfig1
# Ubuntu 26.04:
sudo apt install libicu78
# Ubuntu 24.04 uses libicu74 instead.
```

ICU is required for Unicode/globalization support; do not enable invariant
globalization as a workaround because that changes text and regex behavior.
See [Avalonia's platform requirements](https://docs.avaloniaui.net/docs/supported-platforms)
and [.NET on Ubuntu](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install)
for other distributions and system dependencies.

On Windows, run `app/win-x64/ArcSight.ParserDesigner.CrossPlatform.exe`.
The package is unsigned; corporate application policies can still require IT approval.

## Build from source

Install the .NET 10 SDK. Avalonia dependencies are downloaded from NuGet; no
Windows Desktop SDK or Visual Studio installation is needed for this solution.
Use the cross-platform NuGet configuration explicitly: the older Windows solution
retains its package-free configuration.

```bash
dotnet restore csharp/ArcSight.CrossPlatform.slnx --configfile csharp/ParserDesigner.CrossPlatform/NuGet.Config --packages .nuget-packages
dotnet build csharp/ArcSight.CrossPlatform.slnx -c Release --no-restore
dotnet run --project csharp/ParserDesigner.CrossPlatform -c Release --no-build
```

Publish a self-contained application for a selected platform:

```bash
dotnet publish csharp/ParserDesigner.CrossPlatform -c Release -r linux-x64 --self-contained true --source https://api.nuget.org/v3/index.json -o build/publish/linux-x64
```

Other runtime identifiers are `win-x64`, `linux-arm64`, `osx-arm64` and `osx-x64`.
The macOS targets use the same source, but have not been executed on a Mac in this
release. A distributable macOS `.app` bundle, code signing and notarization are not
included. Mobile and browser targets are not part of this edition.

## Verification

```bash
bash scripts/verify-cross-platform.sh
```

On Windows with PowerShell:

```powershell
./scripts/verify-cross-platform.ps1
```

The UI regression executable requires a graphical session. On a headless Linux
machine, use `xvfb-run -a bash scripts/verify-cross-platform.sh` after installing
Xvfb and the native dependencies. `--skip-ui` (PowerShell: `-SkipUi`) runs the
remaining checks only; it does not establish UI compatibility.

The release was checked on Windows and Ubuntu 26.04 LTS x64 under WSLg.
Tests exercise real native controls: group highlighting, 100-step undo/redo,
searchable help, token snippets/formats, mapping/severity export, submessage
editing, batch routing, filters, selection retention and stale progress handling.
The 69 core regressions cover parser semantics, file round trips, parser suggestions and regex-element instrumentation. See [Parser assistance](PARSER_ASSISTANCE.md) for the new workflows.
Native file pickers use each operating system's desktop integration; picker clicks
are not automated by the regression executable.

## Usage and differences from WinForms

- The wrapping toolbar exposes file, parser and testing actions directly.
- **Message** colors each captured group. Click inside **Regex** or select a
  capture to highlight its original text. Long regexes wrap without inserting newlines.
- **Tokens** includes captured-text snippets, a type selector and a format builder.
- **Submessages** uses a list of definitions and indented patterns. Select a
  definition to edit its ID; select a pattern to edit its regex and mappings.
  Earlier/Later changes evaluation order. Test before applying.
- **Test log file** shows original line numbers, message IDs, selected routes and
  attempted patterns. Edit a submessage or the full parser to retest all loaded logs.
- **Apply** changes the main editor; **Save** writes to disk. Closing a dirty main
  window offers Save, Discard or Cancel. Ctrl+S / Cmd+S saves; F5 tests; F1 opens help.
- AvaloniaEdit provides up to 100 text undo steps and redo. Generated source changes
  are one undo unit. Structural list operations are discarded with Cancel rather
  than undone individually.
- Preview settings remain separate from properties. The parser still uses .NET
  regex locally; verify Java compatibility, conversions and ArcSight operations
  in the connector. No network service or log upload is used by the application.

See the [user guide](USER_GUIDE.md) and offline help for parser concepts and limits.
Platform packaging does not change the `.properties` format.

## Code map

| File | Responsibility |
|---|---|
| `Program.cs` | Platform detection, Fluent theme and application lifecycle |
| `Ui.cs` | Shared layouts, tooltips, dialogs, editors and capture background painter |
| `MainWindow.cs` | File/session state, synchronization, async debugger and editor commands |
| `TokensDialog.cs` | Token draft, asynchronous snippets and Java format builder |
| `MappingDialog.cs` | Direct event assignments and advisory type diagnostics |
| `AdvancedDialog.cs` | Severity and extra processor configuration |
| `SubmessagesDialog.cs` | Ordered definitions, patterns, local tests and group navigation |
| `BatchWindow.cs` | Bounded log loading, summary testing and detailed selected-log evaluation |
| `HelpDialog.cs` | Searchable offline help; topic text is shared with WinForms |
| `ParserDesigner.CrossPlatform.Tests` | Native UI regression executable and screenshots |

All files above are under `csharp/ParserDesigner.CrossPlatform` unless noted.
`ParserCore` remains shared and has no UI dependency. Regex work runs on background
tasks with bounded operations, cancellation and revision checks. Each worker is
serialized so fast edits cannot accumulate concurrent regex executions.

## Publishing

Run verification first, then `scripts/package-cross-platform.ps1` to create a new
source directory with self-contained Linux and Windows applications. The script
refuses an existing destination. Source and runtime dependency notices accompany
the applications; SHA-256 manifests are generated after copying the files.
Runtime updates require republishing the self-contained applications. If code
signing is added, sign before generating the final manifests and release archives.

The third-party PDF is not included in the cross-platform distribution. No project
license has been selected yet; dependency licenses do not license the project itself.
