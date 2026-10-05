# Changelog

## 0.5.2

- Reduce the portable ZIP from 23.6 MB to approximately 2.3 MB. Link the official
  Swift runtime statically into one executable; remove Foundation and ICU dependencies.
- Use CRT-initialized native threads, kernel-event executor wakeups, CreateProcessW
  watchdog startup, ordinal Windows name comparisons and native file I/O.
- Preserve the existing JSON status/recovery schemas with exact integer decoding,
  bounded reads, strict UTF-8, duplicate-key checks and atomic file replacement.
- Add Foundation compatibility tests and native storage self-tests. Retain output
  selection, 5% steps, indicator settings, calibration recovery and capture behavior.
- Remove only previously installed runtime DLLs listed in the old installation manifest.


## 0.5.1 — 2026-10-05

- Add an immediately applied, persistent indicator choice in the tray menu and
  CLI (`osd custom|system`). The default remains BrightnessCtl's custom indicator.
- System mode removes the duplicate BrightnessCtl OSD when hardware keys already
  trigger a Windows/OEM indicator. Existing input, software brightness and DDC
  behavior are preserved. This mode does not synthesize or set a system indicator.
- Custom mode suppresses recognized Windows Shell brightness flyouts after a
  brightness event; volume/media events cancel suppression. Unknown hosts remain
  untouched. This is a compatibility path using optional internal Shell signatures.

## 0.5.0 — 2026-10-05

- Rewrite all application behavior in Swift 6.4: Win32 tray/OSD, dedicated keyboard
  thread, native HID parser, persistence and watchdog recovery.
- Discover GPU-independent Windows display paths; prefer native WDDM scanout gamma
  and retain AMD RGB gain as a fallback. Exclude virtual, cloned and HDR outputs.
- Preserve stable selection and pending calibration recovery. Restore through the
  existing owner before rescan/shutdown, and retry temporarily unavailable drivers.
- Use native Task Scheduler COM, executable validation and per-user startup tasks.
- Bundle required Swift runtimes and licenses; omit developer debug information.
- Preserve current brightness, 5% steps and selected-monitor DDC maximum.

## 0.1.1

- Run the F1/F2 keyboard hook on a dedicated message thread, keeping it responsive
  during slow display-driver calls, UI work and modal dialogs. Brightness work is
  posted to the resident window; modified F1/F2 shortcuts remain available.
- Rearm the hook on its own thread and log installation/message-loop failures.
- Start the installed resident through its matching interactive scheduled task
  from the installer and CLI, keeping it independent of the invoking shell.
- Keep existing brightness, 5% steps and physical-output dimming behavior.

## 0.1

First public release; numbering starts at 0.1 for the public project.

- Per-physical-output AMD RGB gain and 5% configurable brightness steps.
- Explicit connector selection, single-output discovery and disconnection handling.
- Selected-monitor DDC maximum, tray controls, OSD, CLI and local state recovery.
- Opt-in F1/F2 interception; no baked-in monitor model or Windows user paths.
- Modular source, repeatable build/package scripts, hardware-independent tests and CI.
- MIT license and provenance/third-party notices.
