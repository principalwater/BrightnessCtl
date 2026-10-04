param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts' }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '64-bit .NET Framework 4.x compiler is required.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -Filter '*.cs' | ForEach-Object FullName)
$executablePath = Join-Path $OutputDirectory 'BrightnessCtl.exe'
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /debug- /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$executablePath" @sourceFiles
if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $LASTEXITCODE" }
Write-Host "Built $executablePath"
