// SPDX-License-Identifier: MIT

import WinSDK

let systemIndicatorMessage: UINT = 0x8005
// WINEVENT_OUTOFCONTEXT invokes this callback only on the UI thread that
// installed it. That thread also sets/clears the destination before unhooking.
nonisolated(unsafe) private var indicatorDestination: MessageDestination?

private func indicatorShown(
    _ hook: HWINEVENTHOOK?, _ event: DWORD, _ window: HWND?, _ object: LONG,
    _ child: LONG, _ thread: DWORD, _ timestamp: DWORD
) {
    guard let window, object == 0, child == 0 else { return }
    _ = indicatorDestination?.post(
        systemIndicatorMessage, value: Int(bitPattern: window), data: Int(timestamp))
}

/// Watches only the Shell's windows; brightness triggers gate each hide request.
final class SystemIndicator {
    let shellMessage = withWideString("SHELLHOOK") { RegisterWindowMessageW($0) }
    private let destination: MessageDestination
    private var hook: HWINEVENTHOOK?
    private var shellPID: DWORD = 0
    private var brightnessTimestamp: DWORD?
    private typealias WindowBand = @convention(c) (HWND?, UnsafeMutablePointer<DWORD>?) -> Int32
    private let getBand: WindowBand?

    init(destination: MessageDestination) {
        self.destination = destination
        let address = withWideString("user32.dll") { GetModuleHandleW($0) }
            .flatMap { GetProcAddress($0, "GetWindowBand") }
        getBand = address.map { unsafeBitCast($0, to: WindowBand.self) }
        indicatorDestination = destination
        RegisterShellHookWindow(destination.window)
        refreshShell()
    }

    /// Rebinds after Explorer restarts without injecting code into its process.
    func refreshShell() {
        var pid: DWORD = 0
        if let shell = GetShellWindow() { GetWindowThreadProcessId(shell, &pid) }
        guard pid != shellPID || (pid != 0 && hook == nil) else { return }
        if let hook { UnhookWinEvent(hook) }
        hook = nil
        shellPID = pid
        brightnessTimestamp = nil
        if pid != 0 {
            hook = SetWinEventHook(
                DWORD(EVENT_OBJECT_SHOW), DWORD(EVENT_OBJECT_SHOW), nil, indicatorShown, pid, 0,
                DWORD(WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS))
        }
    }

    /// Shell trigger 55 is used for brightness by the tested Windows/OEM path.
    func shellTrigger(_ code: WPARAM, custom: Bool) {
        if code == 55 && custom {
            brightnessTimestamp = DWORD(bitPattern: GetMessageTime())
            hideKnownFlyout()
        } else if code == 12 || code == 56 || !custom {
            brightnessTimestamp = nil
        }
    }

    func shown(_ address: Int, timestamp: DWORD, custom: Bool) {
        guard custom, let brightnessTimestamp,
            timestamp &- brightnessTimestamp <= 500, let window = HWND(bitPattern: address)
        else { return }
        hide(window)
    }

    private func hideKnownFlyout() {
        for className in ["NativeHWNDHost", "XamlExplorerHostIslandWindow"] {
            var previous: HWND?
            while let window = withWideString(
                className,
                { type in
                    withWideString("") { FindWindowExW(nil, previous, type, $0) }
                })
            {
                hide(window)
                previous = window
            }
        }
    }

    private func hide(_ window: HWND) {
        var pid: DWORD = 0
        GetWindowThreadProcessId(window, &pid)
        guard shellPID != 0, pid == shellPID, IsWindowVisible(window), GetWindowTextLengthW(window) == 0 else {
            return
        }
        var className = Array(repeating: WCHAR(0), count: 128)
        GetClassNameW(window, &className, Int32(className.count))
        let name = String(decoding: className.prefix(while: { $0 != 0 }), as: UTF16.self)
        guard ["NativeHWNDHost", "XamlExplorerHostIslandWindow"].contains(name) else { return }
        var band: DWORD = 0
        // GetWindowBand and these Shell signatures are optional internal APIs.
        // Unknown hosts fail closed; never hide generic Shell or media windows.
        guard let getBand, getBand(window, &band) != 0, band == 18 else { return }  // ZBID_ABOVELOCK_UX
        ShowWindowAsync(window, Int32(SW_HIDE))
    }

    deinit {
        indicatorDestination = nil
        if let hook { UnhookWinEvent(hook) }
        DeregisterShellHookWindow(destination.window)
    }
}
