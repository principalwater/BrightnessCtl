# Binary size and packaging

The 0.5.1 executable was about 239 KB; its complete distribution was 23.62 MB
zipped and about 59 MB unpacked. Most bytes belonged to Foundation's transitive
runtime dependencies, including a 38.23 MB Foundation ICU DLL.

## Measured experiments on Swift 6.4.0, Windows x64

| Application imports and linking | Executable | Portable ZIP | Swift DLLs |
|---|---:|---:|---:|
| Foundation, dynamic runtime (0.5.1) | 0.24 MB | 23.62 MB | 15 |
| Foundation, static runtime | 52.76 MB | not selected | 0 |
| FoundationEssentials + Win32, static runtime | 9.68 MB | 3.59 MB | 0 |
| Standard library + Win32, static runtime (0.5.2) | about 6.12 MB | about 2.3 MB | 0 |

Sizes use decimal MB. The ZIP includes the runtime: this is not a small executable
that needs to download a separate Swift installation. Windows and the Microsoft
Visual C++ x64 Redistributable remain prerequisites. The executable is larger
than before because it now contains the runtime; the complete download and
installed binaries are approximately ten times smaller. No executable packer,
self-extracting loader, altered SDK, or runtime DLL pruning is used.

## Implementation

Foundation threads/conditions became CRT-initialized `_beginthreadex` threads and
Win32 events. Blocking display work still runs on a dedicated Swift serial actor
executor; UI and input keep their own message threads. Swift actors, Task, Mutex,
noncopyable resource ownership and InlineArray/Span remain in use.

CreateProcessW starts the recovery watchdog with a quoted executable path, mutable
UTF-16 command line and no inherited handles. Native files use UTF-16 Windows
paths, bounded ReadFile, strict UTF-8 and adjacent unique temporary files. Writes
are flushed and closed before MoveFileExW replaces the destination; a failed commit
leaves the old file intact and removes the temporary file.

`StateJSON` is restricted to the application's existing flat status/recovery
schemas: strings, integers, booleans, nulls and UInt16 gamma arrays. It does not
implement a general JSON library. Integer parsing avoids floating point, including
64-bit process-start timestamps. Old Foundation-generated files remain compatible;
malformed UTF-8/escapes, duplicate keys, overflow and inputs of 32 KiB or more are
rejected. Foundation is used only in tests as an independent compatibility oracle.

The build uses `-Osize`, `-static-stdlib`, `-use-static-resource-dir`, `/OPT:REF` and
`/OPT:ICF`. Swift 6.4's static Concurrency references require explicit SDK
`dispatch.lib` and `BlocksRuntime.lib` link inputs. The SDK is not patched. Debug
information is disabled to avoid embedding developer paths. The build rejects
Swift DLL imports and executables over 6.5 MB; packaging rejects ZIPs over 3 MB.

## Validation and limits

Portable Swift Testing compares both recovery backends with Foundation's JSON
encoder/decoder and covers numeric bounds, Unicode escapes, invalid UTF-8 and
malformed state. `--test-storage` checks Unicode paths, atomic replacement,
failed-commit preservation, temporary-file cleanup, read limits and empty files
without touching a monitor or user settings. `--abi-check` and `--test-input`
exercise imported layouts and the dedicated keyboard thread.

Native gamma hardware support remains driver-dependent. This packaging change
does not establish new Intel/NVIDIA hardware coverage. Static runtime integration
is validated with the pinned 6.4.0 SDK; upgrading the compiler requires repeating
the dependency, recovery and capture checks.

References: [Swift 6.4](https://www.swift.org/blog/swift-6.4-released/),
[Windows SDK changes](https://forums.swift.org/t/upcoming-changes-to-windows-swift-sdks/81313),
[Foundation architecture](https://github.com/swiftlang/swift-foundation),
[_beginthreadex](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/beginthread-beginthreadex),
[CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw),
[MoveFileExW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw).
