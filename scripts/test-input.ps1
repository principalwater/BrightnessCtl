$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -Filter '*.cs' | ForEach-Object FullName)
$testPath = Join-Path $repoRoot 'artifacts\InputTests.exe'
New-Item -ItemType Directory -Path (Split-Path $testPath -Parent) -Force | Out-Null
& $compilerPath /nologo /target:exe /main:BrightnessCtl.InputTests /platform:x64 /optimize+ /debug- /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$testPath" @sourceFiles (Join-Path $repoRoot 'tests\InputTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Input regression check did not compile.' }
& $testPath
if ($LASTEXITCODE -ne 0) { throw 'Input regression check failed.' }
