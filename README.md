# BrightnessCtl

A Windows tray utility written in **Swift 6.4**, for software brightness on one
physical monitor. It scales the selected display's scanout gamma through native
Windows WDDM APIs and keeps its DDC/CI backlight at maximum. AMD ADL RGB gain is
available when the native driver path cannot be opened.

Version **0.5.0** introduces the Swift implementation. Releases 0.1 and 0.1.1
retain their original C# implementation in their tags and release archives.

## Compatibility

- Windows 10/11 x64; one independent physical SDR display.
- Discovery uses Windows display paths and WDDM adapter types for all GPU vendors.
  Virtual/indirect displays, cloned sources and HDR are excluded.
- Native gamma was physically tested on AMD FirePro D700. Intel/NVIDIA use the same
  Windows API but have not yet been tested on hardware. Drivers can reject gamma
  control or ownership, particularly when a fullscreen app owns the output.
- Microsoft Visual C++ 2015–2022 x64 Redistributable is required. Release packages
  include Swift runtime libraries; the compiler is not required.
- Enable DDC/CI in the monitor menu to enforce maximum backlight. Software control
  can work without DDC; `info` reports when maximum backlight is unconfirmed.

Percentages represent software gain, not calibrated nits. Night Light, calibration
loaders, exclusive fullscreen apps and capture drivers can compete for output
controls. Check those workflows on your hardware.

## Install and build

Extract the complete `win-x64.zip` from
[Releases](https://github.com/principalwater/BrightnessCtl/releases) and run
`install.ps1` in PowerShell. It preserves settings and brightness, installs into
`%LOCALAPPDATA%\BrightnessCtl`, and registers interactive sign-in startup.
Administrator rights are not needed. The executable is unsigned.

For a source checkout, install the official
[Swift Windows toolchain](https://www.swift.org/install/windows/), MSVC x64 Build
Tools and Windows SDK, then run:

```powershell
./scripts/build.ps1
./scripts/test.ps1
./scripts/install.ps1
```

## Select a monitor and brightness

The first start selects a unique eligible physical display. If there are several,
choose an ID explicitly. Moving to a different connector can require reselection.
A disconnected saved target never falls back to another monitor. AMD 0.1.x settings
are migrated only when the original target can be identified.

```powershell
./BrightnessCtl.exe list
./BrightnessCtl.exe select '<id-from-list>'
./BrightnessCtl.exe 75
./BrightnessCtl.exe +5
./BrightnessCtl.exe -5
./BrightnessCtl.exe get
./BrightnessCtl.exe info
./BrightnessCtl.exe rescan
./BrightnessCtl.exe exit
```

`Ctrl+Alt+Up/Down` changes brightness by 5 percentage points;
`Ctrl+Alt+PageUp/PageDown` selects 100%/0%. The tray has presets and an OSD.
Bare F1/F2 interception and HID consumer brightness keys are opt-in via `grabF1F2=1`.
The hook has a dedicated thread. HID reports use Windows' HID parser.

Edit `%LOCALAPPDATA%\BrightnessCtl\config.ini` and restart for input changes.
Options: `step`, `up`, `down`, `max`, `min`, `grabF1F2`, `interceptInjectedKeys`,
`restoreOnResume`, `backend=auto|native|amd`, `targetDisplay`. Empty hotkeys disable
bindings. Injected F1/F2 are ignored unless `interceptInjectedKeys=1`. `rescan`
reloads display/backend settings and preserves the current brightness.

## Recovery and capture

Original calibration is stored atomically before dimming. A hidden watchdog
restores it if the tray exits unexpectedly; two processes are normal. Exit restores
output colors while physical backlight stays at maximum. Pending recovery for an
unavailable monitor is retained and cannot be overwritten by selecting another.
If both processes are killed, the next start recovers the available output.

The app controls scanout below desktop composition, with no desktop overlay,
global Magnification matrix or color-profile assignment. Native ownership permits
output duplication. Independent DXGI captures of a white surface retained identical
RGB values at 100%, 45% and 10% on the tested driver. A persistent duplication session
continued delivering changing frames through brightness switches. A complete
Sunshine/Moonlight session still needs checking with the native backend; the AMD
backend was tested with Sunshine/Moonlight.

## Development, privacy and license

All application implementation is Swift. Header-only Clang modules declare native
Windows/AMD ABI; there is no C/C++ helper or C# runtime dependency.
`Sources/BrightnessCore` contains portable gain/selection logic;
`Sources/BrightnessCtl` owns Win32 UI, input, WDDM/DDC/AMD, recovery and native
Task Scheduler COM startup. See [Windows interop notes](docs/WindowsInterop.md).

Swift Testing runs without modifying a monitor. On an interactive desktop,
`scripts/test-input.ps1` injects three F2 taps into its own hook and checks a
2.4-second UI stall without changing brightness. The core also supports
`swift test` with Swift 6.4 on macOS 26 or later.

There is no telemetry, networking, account integration or updater. Local settings,
recovery files, status and logs are excluded from Git and packages. Review logs
before sharing: IDs and error messages can identify devices or local paths.
Release builds omit debug information that could contain developer source paths.

MIT licensed. BetterDisplay and MonitorControl were conceptual references; their
application code was not copied. AMD declarations and bundled Swift runtimes retain
their licenses. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and [Licenses](Licenses).
