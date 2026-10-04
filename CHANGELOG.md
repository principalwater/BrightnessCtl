# Changelog

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

The legacy launch/input lifecycle is being investigated after a resident process
disappeared without a normal shutdown entry. A follow-up fix is planned as 0.1.1.
