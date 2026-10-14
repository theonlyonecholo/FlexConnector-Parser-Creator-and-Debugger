#!/usr/bin/env bash
set -euo pipefail
repo_root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$repo_root"
dotnet restore csharp/ArcSight.CrossPlatform.slnx --configfile csharp/ParserDesigner.CrossPlatform/NuGet.Config --packages .nuget-packages
dotnet build csharp/ArcSight.CrossPlatform.slnx -c Release --no-restore --nologo
dotnet csharp/ParserCore.Tests/bin/Release/net10.0/ParserCore.Tests.dll
if [[ "${1:-}" != "--skip-ui" ]]; then
    dotnet csharp/ParserDesigner.CrossPlatform.Tests/bin/Release/net10.0/ParserDesigner.CrossPlatform.Tests.dll
fi
dotnet format whitespace csharp/ArcSight.CrossPlatform.slnx --no-restore --verify-no-changes --verbosity minimal
