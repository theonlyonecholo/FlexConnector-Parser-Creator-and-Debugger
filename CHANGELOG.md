# Changelog

## 2.0.0 — 2026-10-14

- Added a native Avalonia desktop edition with Windows, Linux and macOS targets.
- Reused ParserCore for properties, regex debugging, token generation, event mapping,
  severity rules, extra processors and ordered submessage routing.
- Added colored message captures, regex group navigation, token snippets, format
  templates, 100-step undo/redo, offline English help and a log-file workbench.
- Added native UI regression tests and self-contained Linux x64 / Windows x64 packaging.
- Verified the native UI on Windows and Ubuntu 26.04 LTS x64 under WSLg.
- Kept the existing Windows WinForms solution available separately. macOS targets
  are available from source but have not been runtime-tested in this release.

## 1.1.0 — 2026-10-14

- Translated all application menus, dialogs, tooltips, accessibility descriptions,
  validation messages and test results into English.
- Translated all eleven searchable offline help topics and the user guide.
- Updated UI regression expectations and screenshots for the English interface.
- Set English framework diagnostic messages without changing the culture used
  for data or regular expressions. Native Windows dialogs follow Windows language settings.
- Prepared C# sources and a matching framework-dependent application package.

Requires Windows and .NET 10 Windows Desktop Runtime. Parser properties, token
names, user log content and ArcSight expressions are preserved as entered.
