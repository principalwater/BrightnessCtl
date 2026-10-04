# Contributing

Build with `scripts/build.ps1` and run `scripts/test.ps1` before proposing changes.
Keep native AMD calls on the resident controller's thread. Never add a global dimming
fallback for unsupported GPUs: virtual streaming outputs must remain unmodified.
Hardware tests should verify 0%, 100%, restoration on exit/crash, reconnection,
multi-monitor targeting and keyboard input. Report GPU model, driver version and
whether HDR/streaming is active; review logs for local identifiers before sharing.

Keep runtime configuration, logs, dumps, credentials and private backup files out of
commits and release archives. Include notices if third-party code is incorporated.
