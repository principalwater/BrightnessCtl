param([string]$OutputDirectory, [string]$SwiftVersion = '6.4.0')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts' }
. (Join-Path $PSScriptRoot 'SwiftEnvironment.ps1') -SwiftVersion $SwiftVersion
Push-Location $repoRoot
try {
    # Optimized SwiftBuild still embeds local paths unless debug info is disabled.
    & swift build -c release -debug-info-format none -Xlinker /SUBSYSTEM:WINDOWS -Xlinker /ENTRY:mainCRTStartup
    if ($LASTEXITCODE -ne 0) { throw 'Swift compilation failed.' }
    $binPath = & swift build -c release -debug-info-format none -Xlinker /SUBSYSTEM:WINDOWS -Xlinker /ENTRY:mainCRTStartup --show-bin-path
    if ($LASTEXITCODE -ne 0) { throw 'Could not locate Swift build products.' }
} finally { Pop-Location }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$sourceExecutable = Join-Path ($binPath | Select-Object -Last 1) 'BrightnessCtl.exe'
Copy-Item -LiteralPath $sourceExecutable -Destination (Join-Path $OutputDirectory 'BrightnessCtl.exe') -Force
# Copy only transitive Swift DLLs. Microsoft/Windows DLLs remain prerequisites.
$queue = [Collections.Generic.Queue[string]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$runtimeFiles = [Collections.Generic.List[string]]::new()
$queue.Enqueue($sourceExecutable)
while ($queue.Count) {
    $dependencyOutput = & dumpbin /nologo /dependents ($queue.Dequeue())
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect native runtime dependencies.' }
    foreach ($line in $dependencyOutput) {
        if ($line -match '^\s*([\w.-]+\.dll)\s*$') {
            $name = $matches[1]
            if ($seen.Add($name) -and $name -match '^(swift|Foundation|_Foundation|BlocksRuntime|dispatch)') {
                $runtimeFile = Join-Path $swiftRuntimeDirectory $name
                if (-not (Test-Path -LiteralPath $runtimeFile)) { throw "Missing Swift runtime: $name" }
                $runtimeFiles.Add($name); $queue.Enqueue($runtimeFile)
                Copy-Item -LiteralPath $runtimeFile -Destination (Join-Path $OutputDirectory $name) -Force
            }
        }
    }
}
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'runtime-files.txt'), ($runtimeFiles | Sort-Object), [Text.Encoding]::UTF8)
Write-Host "Built BrightnessCtl with $($runtimeFiles.Count) Swift runtime libraries."
