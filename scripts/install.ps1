param([switch]$NonInteractive)
# BrightnessCtl installer.
# Put this script next to BrightnessCtl.exe, right-click it and choose
# "Run with PowerShell". No administrator rights are needed.

$ErrorActionPreference = 'Stop'

Write-Host ""
Write-Host "BrightnessCtl installer" -ForegroundColor Cyan
Write-Host "-----------------------"

$src = Join-Path $PSScriptRoot 'BrightnessCtl.exe'
if (-not (Test-Path -LiteralPath $src)) {
    $src = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\BrightnessCtl.exe'
}
if (-not (Test-Path $src)) {
    Write-Host "BrightnessCtl.exe was not found next to this script." -ForegroundColor Red
    Write-Host "Save both files into the same folder and run this again."
    if (-not $NonInteractive) { Read-Host "`nPress Enter to close" }
    exit 1
}

$dir = Join-Path $env:LOCALAPPDATA 'BrightnessCtl'
$exe = Join-Path $dir 'BrightnessCtl.exe'

New-Item -ItemType Directory -Force -Path $dir | Out-Null

# Let the resident restore its output colors and the recovery helper exit
# before replacing the executable. Never terminate the recovery helper first.
if (Test-Path -LiteralPath $exe) {
    $stopBrightness = Start-Process -FilePath $exe -ArgumentList 'exit' -Wait -PassThru -WindowStyle Hidden
    for ($brightnessWait = 0; $brightnessWait -lt 40; $brightnessWait++) {
        $brightnessOld = @(Get-Process BrightnessCtl -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
        if ($brightnessOld.Count -eq 0) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($brightnessOld.Count -gt 0) {
        $brightnessResidents = Get-CimInstance Win32_Process -Filter "Name='BrightnessCtl.exe'" |
            Where-Object { $_.ExecutablePath -eq $exe -and $_.CommandLine -notmatch '--watchdog' }
        foreach ($brightnessResident in $brightnessResidents) {
            Stop-Process -Id $brightnessResident.ProcessId -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 2
    }
    if (Get-Process BrightnessCtl -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) {
        throw 'BrightnessCtl has not finished restoring its output. See startup.log before reinstalling.'
    }
}

Copy-Item $src $exe -Force
Write-Host "  installed to      $exe"

# Autostart, path 1: the classic Run key.
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
                 -Name 'BrightnessCtl' -Value ('"' + $exe + '"')
Write-Host "  Run key           set"

$brightnessTaskRegistered = $false

# Autostart, path 2: a logon task, 20 s after sign-in. Redundant on purpose -
# the app's single-instance guard makes a double launch harmless, and whichever
# mechanism wins, brightness control is up.
try {
    $me        = "$env:USERDOMAIN\$env:USERNAME"
    $action    = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $dir
    $trigger   = New-ScheduledTaskTrigger -AtLogOn -User $me
    $trigger.Delay = 'PT20S'
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                    -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
    $principal = New-ScheduledTaskPrincipal -UserId $me -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName 'BrightnessCtl' -Action $action -Trigger $trigger `
        -Settings $settings -Principal $principal `
        -Description 'Software brightness control; physical monitor brightness held at 100%' -Force | Out-Null
    $brightnessTaskRegistered = $true
    Write-Host "  logon task        registered"
} catch {
    Write-Host ("  logon task        skipped (" + $_.Exception.Message + ")") -ForegroundColor Yellow
    Write-Host "                    the Run key alone is enough"
}

# Task Scheduler starts the resident outside the invoking shell's job. Tools
# and terminals may clean up child processes when their command/session ends.
if ($brightnessTaskRegistered) {
    Start-ScheduledTask -TaskName 'BrightnessCtl'
} else {
    Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + $exe + '"') -WindowStyle Hidden
}
Start-Sleep -Seconds 3

Write-Host ""
if (Get-Process BrightnessCtl -ErrorAction SilentlyContinue) {
    Write-Host "Running." -ForegroundColor Green
    Write-Host "  F1 / F2                  optional: enable grabF1F2=1 in config.ini"
    Write-Host "  Ctrl+Alt+Down / Up       software brightness (configured step)"
    Write-Host "  Ctrl+Alt+PageDown / Up   0% / 100%"
    Write-Host ""
    Write-Host "It will start by itself from now on. If it ever does not,"
    Write-Host "the reason will be in $dir\startup.log"
} else {
    Write-Host "It did not start - see $dir\startup.log" -ForegroundColor Yellow
}

if (-not $NonInteractive) { Read-Host "`nPress Enter to close" }
