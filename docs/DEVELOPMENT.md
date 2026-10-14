# Development and verification

## Prerequisites and configuration

Use Windows and .NET 10 SDK with Windows Desktop reference packs. No external
NuGet packages are required. The solution uses `.slnx`; use an SDK/IDE that supports
this format. `csharp/Directory.Build.props` defines shared nullable, implicit-usings
and warnings-as-errors settings. Individual UI projects enable WinForms and target
`net10.0-windows`; the core targets `net10.0`.

Run commands from the repository root. If a running executable locks build output,
close it or use a separate `--artifacts-path build/<name>` instead of overwriting it.

## One verification command

```powershell
./scripts/verify.ps1
```

The script stops on any failed command and restores the original working directory.
It does not publish, commit, install packages or alter source formatting. Build
artifacts go to `build/verify`; screenshots go to `build/`.

For individual steps:

```powershell
dotnet build csharp/ArcSight.ParserDesigner.slnx -c Release --artifacts-path build/dev
dotnet build/dev/bin/ParserCore.Tests/release/ParserCore.Tests.dll
dotnet build/dev/bin/ParserDesigner.UiTests/release/ParserDesigner.UiTests.dll
dotnet format whitespace csharp/ArcSight.ParserDesigner.slnx --no-restore --verify-no-changes
```

To apply formatting, omit `--verify-no-changes`. Formatting is governed by the
root `.editorconfig`. Generated `bin`/`obj` files are not source files to edit.

## Test ownership

`ParserCore.Tests/Program.cs` is a package-free executable with a small test harness.
It checks properties escaping and preservation, token generation, severity,
processor validation, regex captures/timeouts/cancellation, group source mapping,
submessage routing and edits, log loading, format templates and event assignments.

`ParserDesigner.UiTests/Program.cs` runs on an STA thread and pumps WinForms messages.
It creates real forms, reads controls through reflection and exercises their event
handlers. It verifies capture colors, undo/redo capacity, regex navigation,
submessage editing, batch retesting, token formats, event mapping, F1/search and
layout at minimum sizes. Screenshots use `DrawToBitmap`.

Run UI tests in an interactive Windows session. They are not browser tests and
cannot be replaced with a headless Linux run. Their root discovery supports the
standard project output and the documented `build/<name>` artifact layout; arbitrary
output depths require adjusting root discovery first.

The regression programs return nonzero on failure. `dotnet test` alone will not
execute them. Inspect generated images after visual changes; geometric assertions
do not prove every text label is readable at all DPI settings.

## Adding configuration behavior

1. Implement parsing/validation/source transformation in `ParserCore` first.
2. Work with decoded values and update source through `PropertiesDocument`.
3. Preserve unrelated and unknown extension options. Scope removals narrowly.
4. Add tests for round trips, rejection cases and a realistic example.
5. Add an editor that works on a draft and returns a result only on acceptance.
6. Add the command to the relevant toolbar menu and describe controls with `HelpUi`.
7. Update offline help, module documentation and the Czech usage guide.

## Adding a preview or diagnostic

Capture inputs on the UI thread. Perform expensive work on a worker, honoring
cancellation and time limits. Check the input revision/selection version before
publishing. Return mismatch states separately from invalid configuration and
cancellation. Preserve original UTF-16 positions when trimming or extracting bodies.

If adding real ArcSight expression evaluation, define the supported grammar and
operation list explicitly, add conversion/routing tests and update compatibility
claims. Merely displaying an expression is not executing it.

## Adding types, fields or help

- Types/formats: update `TokenFormats`; retain manual formats and unknown loaded types.
- Event fields: update `EventMapping.Fields` from a verified source; keep custom fields.
- Help topic: add a stable ID in `HelpTopics`, map the form in `HelpUi`, and annotate
  controls after the theme/help installation. Tests currently assert the topic count.
- New text field: use the undo-aware control when editable; reset history at document
  or tree-node boundaries, not after every user change.

## Release preparation

Run verification, inspect UI screenshots and test representative saved parsers in
the intended ArcSight connector. Copy the full framework-dependent output folder.
Keep artifacts, temporary files, local IDE state and machine-specific settings out
of source control. The committed README image is a curated documentation asset;
update it intentionally rather than committing every generated screenshot.

The PDF reference is not a runtime requirement. Its redistribution/license status
is separate from the C# build.
