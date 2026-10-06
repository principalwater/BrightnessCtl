# BrightnessCtl contributor guide

BrightnessCtl is a native Swift Windows tray utility for calibrated brightness on
one physical SDR output. Follow existing patterns and keep changes focused.

Sources/BrightnessCtl contains the Windows application, Sources/BrightnessCore the
portable algorithms, and Sources/WindowsDisplayABI the header-only interop.
Use README.md for compatibility and scripts/build.ps1 and scripts/test.ps1 for
builds/tests. Run native ABI/storage checks on Windows and core tests on macOS.
Build and test sequentially; verify successful exit codes and CI before release.

Preserve calibrated recovery, the watchdog, explicit output identity, native input,
resource ownership and accessibility. Never discard a recovery lease or terminate
the watchdog to unblock an update. Do not select a different display after the
saved output disconnects. Keep native APIs/standard library first, dependencies
minimal and performance claims supported by measurements on the target platform.

Keep source/documentation in English. Preserve licenses and third-party notices.
Repository instructions must be neutral: never include personal agent settings,
private hosts, SSH destinations, account/home paths, credentials or private logs.
Read CONTRIBUTING.md and SECURITY.md for review and disclosure procedures.
