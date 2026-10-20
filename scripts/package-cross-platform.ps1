# Create an independent source tree and matching self-contained native applications.
param(
    [string]$Destination = '',
    [string[]]$Runtimes = @('linux-x64', 'win-x64')
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $repositoryRoot 'GitHub-Ready/ArcSight-Parser-Designer-2.3.5'
}
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw "Destination already exists: $Destination" }
foreach ($rid in $Runtimes) {
    if ($rid -notin @('linux-x64', 'linux-arm64', 'win-x64', 'win-arm64', 'osx-x64', 'osx-arm64')) { throw "Unsupported runtime: $rid" }
}
$cache = Join-Path $repositoryRoot '.nuget-packages'
$project = Join-Path $repositoryRoot 'csharp/ParserDesigner.CrossPlatform/ParserDesigner.CrossPlatform.csproj'
New-Item -ItemType Directory -Path $Destination | Out-Null

Push-Location $repositoryRoot
try {
    foreach ($rid in $Runtimes) {
        $output = Join-Path $Destination "app/$rid"
        dotnet publish $project -c Release -r $rid --self-contained true -o $output --source https://api.nuget.org/v3/index.json "-p:RestorePackagesPath=$cache" --nologo
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid. The destination is incomplete." }
        # Symbols are excluded by Git; keep them out of the release manifest as well.
        Get-ChildItem -LiteralPath $output -Recurse -File -Filter '*.pdb' | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs/CROSS_PLATFORM_DISTRIBUTION.md') -Destination (Join-Path $output 'README.md')
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'examples') -Destination (Join-Path $output 'examples') -Recurse
        $licenses = Join-Path $output 'licenses'
        New-Item -ItemType Directory -Path $licenses | Out-Null
        Copy-Item -Path (Join-Path $repositoryRoot 'docs/licenses/*') -Destination $licenses

        # Include upstream package notices, including bundled native libraries/runtime.
        $assets = Get-Content -LiteralPath (Join-Path $repositoryRoot 'csharp/ParserDesigner.CrossPlatform/obj/project.assets.json') -Raw | ConvertFrom-Json
        $packageDirectories = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { Join-Path $cache $_.Value.path })
        $runtimeRoot = Join-Path $cache "microsoft.netcore.app.runtime.$rid"
        $packageDirectories += @(Get-ChildItem -LiteralPath $runtimeRoot -Directory | Select-Object -ExpandProperty FullName)
        $inventory = [Collections.Generic.List[string]]::new()
        $mit = @'
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
'@
        foreach ($directory in ($packageDirectories | Sort-Object -Unique)) {
            $spec = Get-ChildItem -LiteralPath $directory -Filter '*.nuspec' | Select-Object -First 1
            if (-not $spec) { throw "Package metadata missing: $directory" }
            [xml]$xml = Get-Content -LiteralPath $spec.FullName
            $meta = $xml.package.metadata
            $name = "$($meta.id)-$($meta.version)"
            $inventory.Add("$name | $($meta.license.InnerText) | $($meta.repository.url)")
            if ($meta.license.InnerText -eq 'MIT') {
                $copyright = if ($meta.copyright) { [string]$meta.copyright } else { "Copyright: $($meta.authors)" }
                [IO.File]::WriteAllText((Join-Path $licenses "$name-MIT.txt"), "$copyright`n`n$mit`n")
            }
            foreach ($notice in Get-ChildItem -LiteralPath $directory -File | Where-Object Name -Match '^(LICENSE|LICENCE|THIRD.PARTY|NOTICE)') {
                Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenses "$name-$($notice.Name)")
            }
        }
        [IO.File]::WriteAllLines((Join-Path $licenses 'DEPENDENCIES.txt'), $inventory)
        $manifest = Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName | ForEach-Object {
            (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/')
        }
        [IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $manifest)
    }
    # Copy after publishing so generated dependency lock files agree with the build.
    foreach ($directory in @('csharp', 'docs', 'examples', 'scripts')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repositoryRoot $directory) -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath($repositoryRoot, $file.FullName)
            if ($relative -match '(^|[\\/])(bin|obj|\.vs|screenshots)([\\/]|$)' -or $file.Name -like '*.user') { continue }
            $target = Join-Path $Destination $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }
    foreach ($name in @('.gitignore', '.gitattributes', '.editorconfig', 'README.md', 'CONTRIBUTING.md', 'CHANGELOG.md')) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $name) -Destination (Join-Path $Destination $name)
    }
    $ignore = Join-Path $Destination '.gitignore'
    [IO.File]::WriteAllLines($ignore, @(Get-Content -LiteralPath $ignore | Where-Object { $_ -notin @('__history/', '__recovery/', 'Win32/', 'Win64/', '*.dproj.local', '*.identcache', '*.dcu') }))
    Write-Output "Created source and self-contained applications: $Destination"
}
finally { Pop-Location }
