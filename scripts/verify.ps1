# Build and run the package-free regression executables on Windows.
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repositoryRoot
try {
    dotnet build csharp/ArcSight.ParserDesigner.slnx -c Release --nologo --artifacts-path build/verify
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet build/verify/bin/ParserCore.Tests/release/ParserCore.Tests.dll
    if ($LASTEXITCODE -ne 0) { throw 'Core regression tests failed.' }
    dotnet build/verify/bin/ParserDesigner.UiTests/release/ParserDesigner.UiTests.dll
    if ($LASTEXITCODE -ne 0) { throw 'WinForms UI regression tests failed.' }
    dotnet format whitespace csharp/ArcSight.ParserDesigner.slnx --no-restore --verify-no-changes --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Source formatting check failed.' }
}
finally {
    Pop-Location
}
