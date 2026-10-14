# Create a clean, independent GitHub-ready copy without modifying the source tree.
param([string]$Destination = '')

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $repositoryRoot 'GitHub-Ready/ArcSight-Parser-Designer'
}
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw "Destination already exists: $Destination. Choose a new empty destination." }
$applicationOutput = Join-Path $repositoryRoot 'build/verify/bin/ParserDesigner/release'
foreach ($name in @('ArcSight.ParserDesigner.exe', 'ArcSight.ParserDesigner.dll', 'ParserCore.dll', 'ArcSight.ParserDesigner.deps.json', 'ArcSight.ParserDesigner.runtimeconfig.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $applicationOutput $name))) { throw 'Run scripts/verify.ps1 successfully before packaging.' }
}

New-Item -ItemType Directory -Path $Destination | Out-Null
foreach ($directory in @('csharp', 'docs', 'examples', 'scripts')) {
    $sourceDirectory = Join-Path $repositoryRoot $directory
    foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($repositoryRoot, $file.FullName)
        if ($relative -match '(^|[\\/])(bin|obj|\.vs|__history|__recovery)([\\/]|$)' -or $file.Name -like '*.user') { continue }
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
# Copy only repository metadata and the optional local reference document.
foreach ($file in Get-ChildItem -LiteralPath $repositoryRoot -File -Force) {
    if ($file.Name -in @('.gitignore', '.gitattributes', '.editorconfig', 'README.md', 'CONTRIBUTING.md', 'CHANGELOG.md', 'FlexConn_DevGuide.pdf')) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name)
    }
}
$appDirectory = Join-Path $Destination 'app'
# The publication copy contains only C#; legacy workspace ignore rules are unnecessary.
$ignorePath = Join-Path $Destination '.gitignore'
$ignoreLines = Get-Content -LiteralPath $ignorePath | Where-Object { $_ -notin @('__history/', '__recovery/', 'Win32/', 'Win64/', '*.dproj.local', '*.identcache', '*.dcu') }
[IO.File]::WriteAllLines($ignorePath, $ignoreLines)
$attributesPath = Join-Path $Destination '.gitattributes'
[IO.File]::WriteAllLines($attributesPath, @(Get-Content -LiteralPath $attributesPath | Where-Object { $_ -ne '*.res binary' }))
New-Item -ItemType Directory -Path $appDirectory | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $applicationOutput -File) {
    if ($file.Extension -in @('.exe', '.dll', '.json')) { Copy-Item -LiteralPath $file.FullName -Destination $appDirectory }
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs/DISTRIBUTION.md') -Destination (Join-Path $appDirectory 'README.md')
$hashes = Get-ChildItem -LiteralPath $appDirectory -File | Where-Object Extension -in @('.exe', '.dll', '.json') | Sort-Object Name | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    $hash.Hash.ToLowerInvariant() + '  ' + $_.Name
}
[IO.File]::WriteAllLines((Join-Path $appDirectory 'SHA256SUMS.txt'), $hashes)
Write-Output "Created: $Destination"
Write-Output "Application: $appDirectory"
