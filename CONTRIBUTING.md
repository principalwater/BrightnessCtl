# Contributing

Build with `scripts/build.ps1` and run `scripts/test.ps1` before proposing changes.
Use Swift 6.4 and the official Microsoft SDK; format with `swift-format` and the
checked-in configuration. Keep driver calls on the display actor's dedicated
executor and input callbacks on their own message thread. Never add a global dimming
fallback for unsupported GPUs: virtual streaming outputs must remain unmodified.
Hardware tests should verify 0%, 100%, restoration on exit/crash, reconnection,
multi-monitor targeting and keyboard input. Report GPU model, driver version and
whether HDR/streaming is active; review logs for local identifiers before sharing.

Keep runtime configuration, logs, dumps, credentials and private backup files out of
commits and release archives. Include notices if third-party code is incorporated.
