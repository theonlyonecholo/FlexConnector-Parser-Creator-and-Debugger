# Contributing

Start with [the architecture](docs/ARCHITECTURE.md) and
[development guide](docs/DEVELOPMENT.md). The maintained application lives in
`csharp/`; the publication package contains only this solution and supporting files.

## Code conventions

- Use English identifiers, comments and developer documentation. Keep current Czech
  interface text unless a change explicitly introduces localization.
- Follow `.editorconfig`; run the formatting check before submitting a change.
- Keep configuration logic in `ParserCore` and control/window lifecycle in the UI.
- Describe non-obvious invariants, ownership and compatibility limits rather than
  restating each line of code in comments.
- Preserve unrelated properties, custom options, cancellation and stale-result guards.
- Do not introduce dependencies solely for convenience in this package-free solution.

## Before submitting

Run `./scripts/verify.ps1`. Add focused regression coverage for changed behavior,
especially source round trips, routing and asynchronous UI state. For layout changes,
inspect screenshots at default and minimum supported sizes.

Explain the problem, resulting behavior, verification performed and any remaining
compatibility limitation. Update relevant help and documentation alongside features.
Do not claim that a successful .NET regex preview validates a complete Java/ArcSight
parser or evaluates operations the application only stores.

Do not include build output, local credentials or proprietary log samples. Use
synthetic/anonymized examples that still demonstrate the issue. No repository
license is currently declared; coordinate licensing with the owner before publishing
or redistributing third-party reference files.
