param([switch]$NonInteractive)
# Per-user installation. No administrator rights or Swift compiler required.
$ErrorActionPreference = 'Stop'
$sourceDirectory = $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory 'BrightnessCtl.exe'))) {
    $sourceDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'
}
$sourceExecutable = Join-Path $sourceDirectory 'BrightnessCtl.exe'
$runtimeManifest = Join-Path $sourceDirectory 'runtime-files.txt'
if (-not (Test-Path -LiteralPath $sourceExecutable) -or -not (Test-Path -LiteralPath $runtimeManifest)) { throw 'Extract the complete release ZIP, or run scripts/build.ps1 first.' }
$runtimeFiles = @(Get-Content -LiteralPath $runtimeManifest | Where-Object { $_.Trim() })
foreach ($name in $runtimeFiles) {
    if ($name -notmatch '^(swift|Foundation|_Foundation|BlocksRuntime|dispatch)[\w.-]*\.dll$' -or -not (Test-Path -LiteralPath (Join-Path $sourceDirectory $name))) { throw 'Incomplete or invalid runtime manifest.' }
}
foreach ($name in @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $env:WINDIR "System32\$name"))) { throw 'Install Microsoft Visual C++ 2015-2022 x64 Redistributable: https://aka.ms/vs/17/release/vc_redist.x64.exe' }
}
$directory = Join-Path $env:LOCALAPPDATA 'BrightnessCtl'
$executable = Join-Path $directory 'BrightnessCtl.exe'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$stopExecutable = if (Test-Path -LiteralPath $executable) { $executable } else { $sourceExecutable }
$stop = Start-Process -FilePath $stopExecutable -ArgumentList 'exit' -WindowStyle Hidden -Wait -PassThru
$session = (Get-Process -Id $PID).SessionId
for ($attempt = 0; $attempt -lt 150; $attempt++) {
    $remaining = @(Get-CimInstance Win32_Process -Filter "Name='BrightnessCtl.exe'" | Where-Object { $_.SessionId -eq $session })
    if (-not $remaining.Count) { break }
    Start-Sleep -Milliseconds 100
}
if ($remaining.Count) { throw 'BrightnessCtl is still restoring its output; inspect startup.log and retry after it exits.' }
# Never kill the watchdog to unlock files: it restores the original display state.
Copy-Item -LiteralPath $sourceExecutable -Destination $executable -Force
foreach ($name in $runtimeFiles) { Copy-Item -LiteralPath (Join-Path $sourceDirectory $name) -Destination (Join-Path $directory $name) -Force }
Copy-Item -LiteralPath $runtimeManifest -Destination $directory -Force
$documentationRoot = if (Test-Path -LiteralPath (Join-Path $sourceDirectory 'LICENSE')) { $sourceDirectory } else { Split-Path $PSScriptRoot -Parent }
foreach ($name in @('README.md','CHANGELOG.md','LICENSE','THIRD_PARTY_NOTICES.md','Licenses','docs')) {
    $path = Join-Path $documentationRoot $name
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $directory -Recurse -Force }
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$taskName = 'BrightnessCtl-' + $identity.User.Value
$action = New-ScheduledTaskAction -Execute $executable -WorkingDirectory $directory
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$trigger.Delay = 'PT20S'
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
# Background task priority 7 can interfere with responsive low-level input.
$settings.Priority = 4
$principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description 'Swift software brightness for one physical display' -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $directory 'startup-task.txt'), $taskName, [Text.UTF8Encoding]::new($false))
if (-not (Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run')) { New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Force | Out-Null }
Set-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'BrightnessCtl' -Value ('"' + $executable + '"')
# Remove only the legacy task targeting this user's installation.
$legacyTask = Get-ScheduledTask -TaskName 'BrightnessCtl' -ErrorAction SilentlyContinue
if ($legacyTask -and $legacyTask.Actions.Execute -eq $executable) {
    try {
        $legacyIdentity = [Security.Principal.NTAccount]::new($legacyTask.Principal.UserId).Translate([Security.Principal.SecurityIdentifier]).Value
        if ($legacyIdentity -eq $identity.User.Value) { Unregister-ScheduledTask -TaskName 'BrightnessCtl' -Confirm:$false }
    } catch { Write-Host 'Legacy startup task retained; the single-instance guard prevents duplicate residents.' }
}
Start-ScheduledTask -TaskName $taskName
Start-Sleep -Seconds 3
$running = @(Get-CimInstance Win32_Process -Filter "Name='BrightnessCtl.exe'" | Where-Object { $_.SessionId -eq $session -and $_.ExecutablePath -eq $executable -and $_.CommandLine -notmatch '--watchdog' })
if (-not $running.Count) { throw "BrightnessCtl did not start. Inspect $directory\startup.log" }
Write-Host "Installed and running: $executable"
Write-Host 'Ctrl+Alt+Up/Down: 5% steps by default. Optional bare F1/F2: grabF1F2=1 in config.ini.'
Write-Host 'Existing brightness and settings are preserved.'
if (-not $NonInteractive) { Read-Host 'Press Enter to close' | Out-Null }
