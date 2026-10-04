param([string]$Version = '0.1')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1')
$packageDirectory = Join-Path $repoRoot ("artifacts\BrightnessCtl-$Version-win-x64")
New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'artifacts\BrightnessCtl.exe') -Destination $packageDirectory -Force
foreach ($document in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination $packageDirectory -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $packageDirectory -Force
$archivePath = "$packageDirectory.zip"
Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archivePath -Force
Write-Host "Packaged $archivePath"
