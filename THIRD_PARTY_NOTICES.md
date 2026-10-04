# Third-party notices and provenance

## AMD Display Library interop

The native function signatures, struct layouts and constants in
`src/Backends/AmdOutput.cs` are adapted from AMD's public ADL SDK headers:
[adl_sdk.h](https://github.com/GPUOpen-LibrariesAndSDKs/display-library/blob/master/include/adl_sdk.h),
[adl_structures.h](https://github.com/GPUOpen-LibrariesAndSDKs/display-library/blob/master/include/adl_structures.h).
Those headers carry the following MIT notice. No AMD driver DLL, SDK binary,
sample application, or SDK documentation is redistributed. `atiadlxx.dll` is
loaded from the user's installed AMD display driver, whose license remains separate.

Copyright (c) 2016 - 2022 Advanced Micro Devices, Inc. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## MonitorControl — conceptual reference

[MonitorControl](https://github.com/MonitorControl/MonitorControl) and its
[`Display.setSwBrightness`](https://github.com/MonitorControl/MonitorControl/blob/main/MonitorControl/Model/Display.swift)
were consulted to understand per-display RGB output scaling and baseline restoration.
No Swift source was copied or translated into BrightnessCtl. The Windows/AMD backend,
hotkeys, tray, persistence and recovery helper are independently implemented.
For transparency, MonitorControl's MIT license is reproduced below as published
in [License.txt](https://github.com/MonitorControl/MonitorControl/blob/main/License.txt).
The source file also credits JoniVR, theOneyouseek, waydabber and other contributors.

MIT License

Copyright © 2017

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
## BetterDisplay — inspiration, no redistributed code

[BetterDisplay](https://github.com/waydabber/BetterDisplay) inspired the goal of
software brightness affecting one local monitor. Its current public landing
branch contains documentation and releases, not the current application sources.
The historical `opensource` branch (BetterDummy) has an MIT license, but none of
its code or assets is included here. Reading that branch does not license the
current proprietary application. BrightnessCtl does not claim to be an exact
port, fork, or endorsed version of either project.

## Windows and .NET

Win32 interop declarations were written against Microsoft's public API documentation.
Windows and .NET Framework are system dependencies; their binaries are not bundled.
