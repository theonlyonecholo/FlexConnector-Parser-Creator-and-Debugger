# Publishing this project on GitHub

The prepared directory is an independent repository root: source, documentation,
examples and scripts live together; `app/` contains the compiled application.
The `app/` binaries are intentionally not ignored, so they can be synchronized
alongside the sources. For release downloads, ZIP `app/` and attach it to a GitHub
Release; retain the runtime requirement in the release description.

## First upload

1. Open the prepared directory in GitHub Desktop or another Git client.
2. Initialize a repository in this directory, review the files and create a commit.
3. Publish it to the chosen GitHub repository. No remote, account or credentials
   are preconfigured by this package.

Equivalent local setup from the prepared directory:

```powershell
git init -b main
git add .
git status
git commit -m "Initial C# parser designer"
```

Create/select your remote repository separately, then connect and push it using
your preferred Git client. Do not paste access tokens into source or scripts.

## What is excluded

`.gitignore` excludes temporary build output, SDK caches and IDE settings.
Synthetic `.log` examples remain included because they are
part of the regression tests.

The third-party `FlexConn_DevGuide.pdf` is preserved locally but excluded from Git
by default. The application and its built-in help do not need this PDF to run.
Only publish that reference if you have redistribution permission. No license has
been invented for the repository; the owner must choose one appropriate to the
project.

## Updating a release

Run `scripts/verify.ps1`, create a new package directory with `scripts/package.ps1`,
and update the published sources and `app/` together. This avoids distributing an
EXE that predates the code in the same commit. Regenerate SHA-256 hashes through
the packaging script. Never copy arbitrary DLLs from other build configurations.

The package does not initialize Git or upload files automatically.
