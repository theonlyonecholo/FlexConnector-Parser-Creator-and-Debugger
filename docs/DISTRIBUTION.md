# Original WinForms Windows application

For the new Avalonia 2.0 edition, see the [cross-platform guide](CROSS_PLATFORM.md).

Version **1.1.0** includes an English interface, tooltips and offline help.

Start `ArcSight.ParserDesigner.exe` from this directory. Keep these files together:

- `ArcSight.ParserDesigner.exe` — Windows launcher.
- `ArcSight.ParserDesigner.dll` — desktop application.
- `ParserCore.dll` — parser and configuration logic.
- `ArcSight.ParserDesigner.deps.json` — dependency metadata.
- `ArcSight.ParserDesigner.runtimeconfig.json` — runtime requirements.

This is a **framework-dependent** Release build and requires the **.NET 10 Windows
Desktop Runtime**. The SDK is not needed to run it. The .NET runtime itself is not
included. If Windows reports a missing runtime, install that runtime before starting.
No PDF, external parser file or package installation is required
to start the application. Use the repository's `examples` for sample logs/parsers.

`SHA256SUMS.txt` records the packaged application file hashes. When distributing,
copy or ZIP the whole application directory, not just the EXE. For a GitHub Release,
upload that ZIP as the downloadable application asset.

To rebuild from source, run `scripts/verify.ps1` at the repository root. Run
`scripts/package.ps1 -Destination <new-directory>` after successful verification
to create another clean copy with matching compiled output. The packaging command
does not overwrite an existing destination.
