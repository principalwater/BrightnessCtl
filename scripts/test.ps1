$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $repoRoot 'artifacts\tests'
New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -Filter '*.cs' | ForEach-Object FullName)
$testFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests') -Filter '*.cs' | ForEach-Object FullName)
$testExecutable = Join-Path $testDirectory 'Tests.exe'
& $compilerPath /nologo /target:exe /platform:x64 /optimize+ /debug- /main:BrightnessCtl.Tests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$testExecutable" @sourceFiles @testFiles
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExecutable
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
