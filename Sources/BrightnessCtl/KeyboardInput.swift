// SPDX-License-Identifier: MIT

import Synchronization
import WinSDK
import WindowsDisplayABI

// Set, used and cleared exclusively on the thread that owns WH_KEYBOARD_LL.
// Windows invokes this hook on that thread; no UI/driver thread accesses it.

private struct InputState {
    let destination: MessageDestination
    let allowInjected: Bool
    var swallowed: UInt8 = 0
    var hook: HHOOK?
}
nonisolated(unsafe) private var inputState: UnsafeMutablePointer<InputState>?
/// Hook callbacks contain no allocation, I/O, actor hop, or display-driver call.
private func keyboardCallback(_ code: Int32, _ message: WPARAM, _ data: LPARAM) -> LRESULT {
    guard code >= 0, let context = inputState,
        let pointer = UnsafePointer<KBDLLHOOKSTRUCT>(bitPattern: Int(data))
    else {
        return CallNextHookEx(nil, code, message, data)
    }
    let key = pointer.pointee
    guard key.vkCode == 112 || key.vkCode == 113,
        context.pointee.allowInjected || (key.flags & 0x10) == 0
    else { return CallNextHookEx(nil, code, message, data) }
    let bit: UInt8 = key.vkCode == 112 ? 1 : 2
    let down = message == WPARAM(WM_KEYDOWN) || message == WPARAM(WM_SYSKEYDOWN)
    let up = message == WPARAM(WM_KEYUP) || message == WPARAM(WM_SYSKEYUP)
    if up && (context.pointee.swallowed & bit) != 0 {
        context.pointee.swallowed &= ~bit
        return 1  // Match the suppressed keydown, even if modifiers changed.
    }
    if down {
        let held = (context.pointee.swallowed & bit) != 0
        let modified =
            GetAsyncKeyState(17) < 0 || GetAsyncKeyState(18) < 0 || GetAsyncKeyState(16) < 0 || GetAsyncKeyState(91) < 0
            || GetAsyncKeyState(92) < 0
        if held || (!modified && !IsHungAppWindow(context.pointee.destination.window)) {
            let posted = context.pointee.destination.post(keyStepMessage, value: key.vkCode == 113 ? 1 : -1)
            if posted || held {
                context.pointee.swallowed |= bit
                return 1
            }
        }
    }
    return CallNextHookEx(nil, code, message, data)
}
final class KeyboardInput: @unchecked Sendable {
    private struct Status {
        var threadID: DWORD = 0
        var active = false
    }
    private let status = Mutex(Status())
    private let finished: OwnedHandle
    private let ready: OwnedHandle
    private let destination: MessageDestination
    private let enabled: Bool
    private let allowInjected: Bool
    private var thread: NativeThread?

    init(destination: MessageDestination, enabled: Bool, allowInjected: Bool) throws {
        self.destination = destination
        self.enabled = enabled
        self.allowInjected = allowInjected
        ready = try OwnedHandle(CreateEventW(nil, true, false, nil))
        finished = try OwnedHandle(CreateEventW(nil, true, false, nil))
        thread = try NativeThread(name: "BrightnessCtl keyboard input") { [weak self] in self?.run() }
        guard WaitForSingleObject(ready.raw, 5000) == DWORD(WAIT_OBJECT_0) else {
            throw WindowsError.unsupported("Keyboard thread did not start.")
        }
        if enabled && !active {
            stop()
            throw WindowsError.api("SetWindowsHookEx", GetLastError())
        }
    }

    var active: Bool { status.withLock { $0.active } }

    private func run() {
        status.withLock { $0.threadID = GetCurrentThreadId() }
        var message = MSG()
        PeekMessageW(&message, nil, 0, 0, UINT(PM_NOREMOVE))
        var context = InputState(destination: destination, allowInjected: allowInjected)
        withUnsafeMutablePointer(to: &context) { pointer in
            inputState = pointer
            defer {
                if let hook = pointer.pointee.hook { UnhookWindowsHookEx(hook) }
                inputState = nil
                status.withLock { $0.active = false }
                SetEvent(finished.raw)
            }
            func rearm() {
                guard enabled else { return }
                if let replacement = SetWindowsHookExW(
                    Int32(WH_KEYBOARD_LL), keyboardCallback, GetModuleHandleW(nil), 0)
                {
                    let previous = pointer.pointee.hook
                    pointer.pointee.hook = replacement
                    if let previous { UnhookWindowsHookEx(previous) }
                    status.withLock { $0.active = true }
                }
            }
            rearm()
            let timer = SetTimer(nil, 0, 60000, nil)
            defer { if timer != 0 { KillTimer(nil, timer) } }
            SetEvent(ready.raw)
            while BC_GetMessageW(&message, nil, 0, 0) > 0 {
                if message.message == UINT(WM_TIMER) {
                    rearm()
                } else {
                    TranslateMessage(&message)
                    DispatchMessageW(&message)
                }
            }
        }
    }

    func stop() {
        let id = status.withLock { $0.threadID }
        if id != 0 {
            PostThreadMessageW(id, UINT(WM_QUIT), 0, 0)
            WaitForSingleObject(finished.raw, 2000)
        }
    }
}
