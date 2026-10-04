# BrightnessCtl

A small Windows tray utility for **software brightness on one physical AMD display**.
It scales that display's RGB output through the AMD driver, while keeping the
selected monitor's DDC/CI backlight at 100%. Other displays and virtual streaming
outputs are not selected. No desktop-wide filter or dimming overlay is used.

## Compatibility

- 64-bit Windows 10/11 and .NET Framework 4.x.
- An installed AMD display driver exposing `atiadlxx.dll` and ADL color controls.
- Tested on AMD FirePro D700 with an SDR external display and Sunshine/Moonlight.
- Other AMD hardware/driver combinations need testing. NVIDIA, Intel, DisplayLink,
  HDR brightness upscaling and monitor groups are not implemented in this release.
- DDC/CI must be enabled in the monitor menu for backlight enforcement. Software
  dimming may still work when DDC is unavailable; `info` reports this explicitly.

This is an early release. The configured percentage is a software gain setting,
not a calibrated measurement in nits. AMD control quantization can affect very low
levels. Color-critical or HDR workflows should test compatibility first.

## Download and install

Get the `win-x64.zip` from [Releases](https://github.com/principalwater/BrightnessCtl/releases),
extract it, and run `install.ps1` using PowerShell. Administrator rights are not required.
The installer copies the executable to `%LOCALAPPDATA%\BrightnessCtl`, preserves
existing settings and registers sign-in startup. The executable is unsigned.

For a source checkout:

```powershell
./scripts/build.ps1
./scripts/test.ps1
./scripts/install.ps1
```

The build uses the compiler supplied with Windows .NET Framework. No third-party
NuGet packages or redistributed GPU SDK/driver binaries are required.

## Pick a display

With one connected AMD physical display, the first start selects and saves it.
With multiple displays, select a physical output explicitly before starting:

```powershell
./BrightnessCtl.exe list
./BrightnessCtl.exe select '<output-id-from-list>'
./BrightnessCtl.exe
```

Run `exit` before changing the target. The identifier contains the GPU connector
and panel name; it does not follow whichever display becomes primary. Reconnecting
on a different connector requires selecting that output again. A disconnected
saved target never falls back to another monitor. Older local configurations using
`targetMonitor`/`targetMonitorId` are migrated when a unique matching output is found.

## Brightness and shortcuts

```powershell
./BrightnessCtl.exe get
./BrightnessCtl.exe 75
./BrightnessCtl.exe +5
./BrightnessCtl.exe -5
./BrightnessCtl.exe info
./BrightnessCtl.exe rescan
./BrightnessCtl.exe exit
```

`Ctrl+Alt+Up/Down` changes brightness by 5 percentage points by default.
`Ctrl+Alt+PageUp/PageDown` selects 100%/0%. Tray presets and an OSD are available.
Bare F1/F2 interception is **off by default**; enable `grabF1F2=1` if desired.
The keyboard hook has a dedicated message thread, so slow display calls and modal
UI do not remove it through Windows' low-level-hook timeout. Installed CLI launches
use the matching scheduled task to keep the resident outside short-lived shell jobs.
The legacy HID consumer decoder supports a specific three-byte report layout;
other keyboards can use configurable Windows hotkeys.

Settings live in `%LOCALAPPDATA%\BrightnessCtl\config.ini`. Restart after editing.
Use `step`, `up`, `down`, `max`, `min`, `restoreOnResume`, `grabF1F2`, and `targetOutput`.
Leave a hotkey value empty to disable it. The monitor's physical backlight stays at
maximum after exit, while the original AMD color controls are restored.

## Recovery and streaming

The original output color controls are recorded atomically before dimming. A hidden
`--watchdog` helper restores them after an unexpected resident exit. Two processes
are therefore normal: the tray and recovery helper. If both are forcibly terminated,
the next start uses the saved original controls rather than multiplying dimming.

On the tested SDR setup, Sunshine/Moonlight captures retained their normal brightness,
and Windows shell UI dimmed uniformly on the physical panel. Capture pipelines and
drivers vary; check your own setup. No global Magnification matrix, color profile
assignment or GDI gamma ramp is changed by this implementation.

## Privacy

The app has no telemetry, network client, account integration or automatic updates.
Runtime settings, color-recovery state and `startup.log` remain on the local machine.
Logs/diagnostics may contain local display IDs and file locations; review them before
posting an issue. These files, old local backups, binaries and build output are ignored
by Git and are not part of the repository. Release packages contain only the executable,
installer and public documentation/licenses.

## Development and license

`src/` separates AMD interop, software gain/recovery, hardware DDC, configuration,
input, tray UI and startup. `scripts/test.ps1` runs tests without modifying a display.
The GitHub Actions workflow builds and runs these tests on Windows.
On an interactive desktop, `scripts/test-input.ps1` checks that captured F1 events
survive a deliberately blocked UI thread. It does not change monitor brightness.

MIT licensed; see [LICENSE](LICENSE). BetterDisplay and MonitorControl were conceptual
references, not copied applications. AMD interop declarations retain AMD's MIT notice.
Details and full notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
