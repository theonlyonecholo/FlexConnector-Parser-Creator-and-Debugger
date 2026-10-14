# Build the native Avalonia application and run core and desktop UI regressions.
param([switch]$SkipUi)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repositoryRoot
try {
    dotnet restore csharp/ArcSight.CrossPlatform.slnx --configfile csharp/ParserDesigner.CrossPlatform/NuGet.Config --packages .nuget-packages
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet build csharp/ArcSight.CrossPlatform.slnx -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet csharp/ParserCore.Tests/bin/Release/net10.0/ParserCore.Tests.dll
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    if (-not $SkipUi) {
        dotnet csharp/ParserDesigner.CrossPlatform.Tests/bin/Release/net10.0/ParserDesigner.CrossPlatform.Tests.dll
        if ($LASTEXITCODE -ne 0) { throw 'Native UI tests failed.' }
    }
    dotnet format whitespace csharp/ArcSight.CrossPlatform.slnx --no-restore --verify-no-changes --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Formatting check failed.' }
}
finally { Pop-Location }
