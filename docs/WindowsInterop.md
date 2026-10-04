# Windows interop in Swift 6.4

BrightnessCtl uses official WinSDK and Clang imports for native layouts. Its
header-only module adds declarations for WDDM, HID, AMD and COM; no C implementation.

References:

- [Swift on Windows](https://www.swift.org/blog/swift-on-windows/): native Win32
  UI from Swift. Its 2020 package-manager limitations are historical.
- [Windows interoperability](https://www.swift.org/blog/swift-everywhere-windows-interop/):
  WinSDK, COM's C ABI and DirectX. Proposed `@COM` syntax is a design idea, not an
  implemented feature used here.
- [Windows workgroup](https://www.swift.org/blog/announcing-windows-workgroup/):
  platform maintenance, Foundation and toolchain interoperability.
- [C library usability](https://www.swift.org/blog/improving-usability-of-c-libraries-in-swift/):
  API notes and `swift-synthesize-interface` importer inspection.
- [SwiftCOM](https://github.com/compnerd/swift-com) and
  [DXSample](https://github.com/compnerd/DXSample) demonstrate COM/DirectX techniques.
  They are references, not dependencies or copied code.

## Ownership and concurrency

Native handles, WDDM sessions, BSTRs and COM references use noncopyable Swift types
with deterministic `deinit`. UTF-16 strings, gamma buffers and device-info pointers
stay within scoped closures. Release `--abi-check` verifies native SDK layouts.

Gamma storage uses `InlineArray` and scoped `Span` access. A dedicated serial actor
executor owns blocking driver calls. Win32 UI and low-level input own separate
message threads. Hook callbacks have no allocation, I/O or driver calls. Mutexes
protect shared completion state; unchecked Sendable declarations explain their
ownership invariant.

## A WinSDK overlay limitation

Swift 6.4 imports `GetMessageW` and `TrackPopupMenu` as Bool. Windows defines signed
-1/0/positive results for `GetMessageW`, and `TPM_RETURNCMD` returns the selected
menu command ID. Bool loses information required by these contracts.

`WindowsDisplayABI.h` declares signed-result aliases bound to the same DLL symbols.
No Windows DLL or Swift SDK file is patched. A destroyed HWND verifies that -1 is
preserved. The aliases target supported x64; 32-bit stdcall needs other decoration.
This is a focused candidate for an upstream overlay improvement with compatibility
tests for existing Bool callers and the two native contracts.

## Output boundary

Discovery excludes virtual/indirect adapters, shared clone sources and HDR.
`D3DKMTSetGammaRamp` uses EMULATED ownership with `AllowOutputDuplication=1`.
API success alone does not prove physical dimming or capture isolation: hardware
checks remain necessary. Native scanout was exercised on AMD hardware; Intel and
NVIDIA require hardware verification.

Recovery restores calibration through the existing owner before destroying it.
It never opens a competing owner while the backend holds the source, or overwrites
an unrecovered lease with another monitor's baseline.
