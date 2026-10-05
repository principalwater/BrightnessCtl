// SPDX-License-Identifier: MIT

import BrightnessCore
import Synchronization
import WinSDK
import WindowsDisplayABI

private let trayMessage: UINT = 0x8004
private final class HardwareGate: Sendable { let busy = Mutex(false) }
private func windowProcedure(_ window: HWND?, _ message: UINT, _ value: WPARAM, _ data: LPARAM) -> LRESULT {
    guard let window else { return DefWindowProcW(window, message, value, data) }
    if message == UINT(WM_NCCREATE), let creation = UnsafePointer<CREATESTRUCTW>(bitPattern: Int(data)),
        let context = creation.pointee.lpCreateParams
    {
        SetWindowLongPtrW(window, Int32(GWLP_USERDATA), LONG_PTR(Int(bitPattern: context)))
    }
    let address = GetWindowLongPtrW(window, Int32(GWLP_USERDATA))
    if address != 0, let context = UnsafeRawPointer(bitPattern: Int(address)) {
        return Unmanaged<TrayApplication>.fromOpaque(context).takeUnretainedValue().handle(window, message, value, data)
    }
    return DefWindowProcW(window, message, value, data)
}
/// Thread-affine Win32 UI. Only Sendable messages and values leave this thread.
final class TrayApplication {
    private let controller: DisplayController
    private var settings: Settings
    private var state: DisplayState
    private var window: HWND?
    private var osd: HWND?
    private var keyboard: KeyboardInput?
    private var systemIndicator: SystemIndicator?
    private var tray = NOTIFYICONDATAW()
    private var trayAdded = false
    private var exiting = false
    private var pendingSteps = 0
    private var lastApply: UInt64 = 0
    private var lastHardwareCheck: UInt64 = 0
    private let hardwareGate = HardwareGate()
    private let taskbarCreated = withWideString("TaskbarCreated") { RegisterWindowMessageW($0) }

    init(settings: Settings) throws {
        self.settings = settings
        controller = try DisplayController(settings: settings)
        do { state = try displayCommand(controller, command: 0) } catch {
            Diagnostics.write("initial display unavailable: \(error)")
            state = DisplayState(level: settings.brightness, device: "", backend: "unavailable", connected: false)
        }
        var initialized = false
        defer {
            if !initialized {
                shutdown()
                if let window { DestroyWindow(window) }
            }
        }
        try registerWindowClass("BrightnessCtl.Control")
        try registerWindowClass("BrightnessCtl.OSD")
        window = try createWindow(
            "BrightnessCtl.Control", title: controlWindowTitle, style: 0, extended: DWORD(WS_EX_TOOLWINDOW))
        osd = try createWindow(
            "BrightnessCtl.OSD", title: "BrightnessCtl", style: DWORD(WS_POPUP),
            extended: DWORD(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TOPMOST))
        guard let window, let osd else { throw WindowsError.api("CreateWindow", GetLastError()) }
        SetLayeredWindowAttributes(osd, 0, 235, DWORD(LWA_ALPHA))
        tray.cbSize = DWORD(MemoryLayout<NOTIFYICONDATAW>.size)
        tray.hWnd = window
        tray.uID = 1
        tray.uFlags = UINT(NIF_MESSAGE | NIF_ICON | NIF_TIP)
        tray.uCallbackMessage = trayMessage
        tray.hIcon = LoadIconW(nil, UnsafePointer<WCHAR>(bitPattern: 32516))
        updateTray()
        for (index, hotkey) in settings.hotkeys.enumerated() {
            if let combo = parseHotkey(hotkey),
                !RegisterHotKey(window, Int32(index + 1), combo.modifiers | UINT(MOD_NOREPEAT), combo.key)
            {
                Diagnostics.write("input: configured hotkey \(index + 1) is already registered")
            }
        }
        if settings.grabFunctionKeys {
            var raw = RAWINPUTDEVICE(usUsagePage: 0x0C, usUsage: 1, dwFlags: DWORD(RIDEV_INPUTSINK), hwndTarget: window)
            RegisterRawInputDevices(&raw, 1, UINT(MemoryLayout<RAWINPUTDEVICE>.size))
        }
        keyboard = try KeyboardInput(
            destination: MessageDestination(window), enabled: settings.grabFunctionKeys,
            allowInjected: settings.interceptInjectedKeys)
        systemIndicator = SystemIndicator(destination: MessageDestination(window))
        SetTimer(window, 1, 3000, nil)
        queueHardwareMaximum()
        Diagnostics.write(
            "start: \(AppVersion.implementation) \(AppVersion.string); dedicated input active=\(keyboard?.active ?? false)"
        )
        initialized = true
    }

    private func registerWindowClass(_ name: String) throws {
        var type = WNDCLASSEXW()
        type.cbSize = UINT(MemoryLayout<WNDCLASSEXW>.size)
        type.lpfnWndProc = windowProcedure
        type.hInstance = GetModuleHandleW(nil)
        type.hCursor = LoadCursorW(nil, UnsafePointer<WCHAR>(bitPattern: 32512))
        let atom = withWideString(name) {
            type.lpszClassName = $0
            return RegisterClassExW(&type)
        }
        guard atom != 0 || GetLastError() == DWORD(ERROR_CLASS_ALREADY_EXISTS) else {
            throw WindowsError.api("RegisterClass", GetLastError())
        }
    }

    private func createWindow(_ type: String, title: String, style: DWORD, extended: DWORD) throws -> HWND {
        let context = Unmanaged.passUnretained(self).toOpaque()
        let handle = withWideString(type) { className in
            withWideString(title) {
                CreateWindowExW(extended, className, $0, style, 0, 0, 280, 92, nil, nil, GetModuleHandleW(nil), context)
            }
        }
        guard let handle else { throw WindowsError.api("CreateWindow", GetLastError()) }
        return handle
    }

    func run() throws {
        var message = MSG()
        while true {
            let result = BC_GetMessageW(&message, nil, 0, 0)
            if result == 0 { break }
            if result < 0 { throw WindowsError.api("GetMessage", GetLastError()) }
            TranslateMessage(&message)
            DispatchMessageW(&message)
        }
    }

    func handle(_ window: HWND, _ message: UINT, _ value: WPARAM, _ data: LPARAM) -> LRESULT {
        if window == osd {
            if message == UINT(WM_PAINT) {
                paintOSD(window)
                return 0
            }
            if message == UINT(WM_TIMER) {
                KillTimer(window, 1)
                ShowWindow(window, Int32(SW_HIDE))
                return 0
            }
            return DefWindowProcW(window, message, value, data)
        }
        if message == taskbarCreated {
            systemIndicator?.refreshShell()
            trayAdded = false
            updateTray()
            return 0
        }
        if message == systemIndicator?.shellMessage {
            systemIndicator?.shellTrigger(value, custom: settings.indicator == .custom)
            return 0
        }
        switch message {
        case systemIndicatorMessage:
            systemIndicator?.shown(
                Int(Int64(bitPattern: value)), timestamp: DWORD(truncatingIfNeeded: data),
                custom: settings.indicator == .custom)
            return 0
        case brightnessMessage:
            guard !exiting else { return 0 }
            if value == 5 {
                do {
                    settings.indicator = try Settings().indicator
                    if let osd { ShowWindow(osd, Int32(SW_HIDE)) }
                    return LRESULT(state.level.percent + 1)
                } catch {
                    Diagnostics.write("indicator: \(error)")
                    return 0
                }
            }
            if value == 4 {
                PostMessageW(window, UINT(WM_CLOSE), 0, 0)
                return 1
            }
            return apply(command: Int(value), value: Int(data), show: value == 1 || value == 2)
                ? LRESULT(state.level.percent + 1) : 0
        case keyStepMessage:
            if !exiting { queue(Int(Int64(bitPattern: value))) }
            return 0
        case UINT(WM_HOTKEY):
            if value == 1 {
                queue(1)
            } else if value == 2 {
                queue(-1)
            } else if value == 3 {
                _ = apply(command: 1, value: 100, show: true)
            } else if value == 4 {
                _ = apply(command: 1, value: 0, show: true)
            }
            return 0
        case UINT(WM_INPUT):
            if settings.grabFunctionKeys && !exiting { handleRawInput(HRAWINPUT(bitPattern: Int(data))) }
        case UINT(WM_TIMER):
            guard !exiting else { return 0 }
            if value == 2 {
                flushSteps()
            } else {
                _ = apply(command: 0, show: false)
                systemIndicator?.refreshShell()
                updateTray()
                queueHardwareMaximum()
            }
            return 0
        case UINT(WM_DISPLAYCHANGE):
            if !exiting {
                _ = apply(command: 3, show: false)
                queueHardwareMaximum()
            }
            return 0
        case UINT(WM_POWERBROADCAST):
            if value == WPARAM(PBT_APMRESUMEAUTOMATIC) || value == WPARAM(PBT_APMRESUMESUSPEND) {
                _ = apply(command: settings.restoreOnResume ? 3 : 1, value: 100, show: false)
                queueHardwareMaximum()
            }
            return 1
        case trayMessage:
            if UINT(truncatingIfNeeded: data) == UINT(WM_RBUTTONUP) {
                showMenu()
            } else if UINT(truncatingIfNeeded: data) == UINT(WM_LBUTTONUP) {
                showOSD()
            }
            return 0
        case UINT(WM_QUERYENDSESSION): return 1
        case UINT(WM_ENDSESSION):
            if value != 0 {
                shutdown()
                DestroyWindow(window)
            }
            return 0
        case UINT(WM_CLOSE):
            shutdown()
            DestroyWindow(window)
            return 0
        case UINT(WM_DESTROY):
            PostQuitMessage(0)
            return 0
        default: break
        }
        return DefWindowProcW(window, message, value, data)
    }

    private func apply(command: Int, value: Int = 0, show: Bool) -> Bool {
        guard !exiting else { return false }
        do {
            state = try displayCommand(controller, command: command, value: value)
            lastApply = GetTickCount64()
            if command == 3 { settings.indicator = try Settings().indicator }
            updateTray()
            if show { showOSD() }
            return true
        } catch {
            Diagnostics.write("software: \(error)")
            return false
        }
    }

    private func queue(_ steps: Int) {
        pendingSteps = max(-20, min(20, pendingSteps + max(-20, min(20, steps))))
        if GetTickCount64() - lastApply >= 130 { flushSteps() } else if let window { SetTimer(window, 2, 130, nil) }
    }
    private func flushSteps() {
        if let window { KillTimer(window, 2) }
        let steps = pendingSteps
        pendingSteps = 0
        if steps != 0 { _ = apply(command: 2, value: steps * settings.step, show: true) }
    }

    private func updateTray() {
        setWideString(
            "BrightnessCtl — \(state.level.percent)%\(state.connected ? "" : " (disconnected)")", in: &tray.szTip)
        if trayAdded {
            Shell_NotifyIconW(DWORD(NIM_MODIFY), &tray)
        } else {
            trayAdded = Shell_NotifyIconW(DWORD(NIM_ADD), &tray)
        }
    }

    private func showMenu() {
        guard let window, let menu = CreatePopupMenu() else { return }
        defer { DestroyMenu(menu) }
        for (id, label) in [
            (0, "Brightness: \(state.level.percent)%"), (1, "Increase by \(settings.step)%"),
            (2, "Decrease by \(settings.step)%"),
            (100, "100%"), (75, "75%"), (50, "50%"), (25, "25%"), (10, "10%"), (3, "Reconnect display"),
        ] {
            _ = withWideString(label) {
                AppendMenuW(menu, UINT(MF_STRING | (id == 0 ? MF_DISABLED : 0)), UINT_PTR(id), $0)
            }
        }
        for (id, mode, label) in [
            (2001, IndicatorMode.custom, "Indicator: BrightnessCtl"),
            (2002, IndicatorMode.system, "Indicator: Windows"),
        ] {
            _ = withWideString(label) {
                AppendMenuW(menu, UINT(MF_STRING | (settings.indicator == mode ? MF_CHECKED : 0)), UINT_PTR(id), $0)
            }
        }
        CheckMenuRadioItem(menu, 2001, 2002, settings.indicator == .custom ? 2001 : 2002, UINT(MF_BYCOMMAND))
        _ = withWideString("Quit") { AppendMenuW(menu, UINT(MF_STRING), 4, $0) }
        var point = POINT()
        GetCursorPos(&point)
        SetForegroundWindow(window)
        let selected = BC_TrackPopupMenu(menu, UINT(TPM_RETURNCMD | TPM_RIGHTBUTTON), point.x, point.y, 0, window, nil)
        if selected == 1 {
            queue(1)
        } else if selected == 2 {
            queue(-1)
        } else if selected == 3 {
            _ = apply(command: 3, show: false)
        } else if selected == 4 {
            PostMessageW(window, UINT(WM_CLOSE), 0, 0)
        } else if selected == 2001 || selected == 2002 {
            do {
                try settings.setIndicator(selected == 2001 ? .custom : .system)
                if let osd { ShowWindow(osd, Int32(SW_HIDE)) }
            } catch { Diagnostics.write("indicator: \(error)") }
        } else if [10, 25, 50, 75, 100].contains(selected) {
            _ = apply(command: 1, value: Int(selected), show: true)
        }
        PostMessageW(window, UINT(WM_NULL), 0, 0)
    }

    private func showOSD() {
        guard settings.indicator == .custom else { return }
        guard let osd, let monitor = monitorForDevice(state.device) else { return }
        var info = MONITORINFO()
        info.cbSize = DWORD(MemoryLayout<MONITORINFO>.size)
        guard GetMonitorInfoW(monitor, &info) else { return }
        // Move invisibly to the target first so per-monitor DPI refers to it.
        SetWindowPos(
            osd, nil, info.rcWork.left, info.rcWork.top, 0, 0, UINT(SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSIZE))
        let scale = Double(GetDpiForWindow(osd)) / 96
        let width = Int32(280 * scale)
        let height = Int32(92 * scale)
        let area = info.rcWork
        SetWindowPos(
            osd, HWND(bitPattern: -1), area.left + (area.right - area.left - width) / 2,
            area.bottom - height - Int32(110 * scale), width, height, UINT(SWP_NOACTIVATE | SWP_SHOWWINDOW))
        InvalidateRect(osd, nil, false)
        SetTimer(osd, 1, 1100, nil)
    }

    private func paintOSD(_ window: HWND) {
        var paint = PAINTSTRUCT()
        guard let dc = BeginPaint(window, &paint) else { return }
        defer { EndPaint(window, &paint) }
        var area = RECT()
        GetClientRect(window, &area)
        let scale = Double(GetDpiForWindow(window)) / 96
        func fill(_ rectangle: RECT, _ color: COLORREF) {
            let brush = CreateSolidBrush(color)
            defer { DeleteObject(brush) }
            var rectangle = rectangle
            FillRect(dc, &rectangle, brush)
        }
        fill(area, 0x001B_1818)
        SetBkMode(dc, Int32(TRANSPARENT))
        SetTextColor(dc, 0x00F5_F0F0)
        let font = withWideString("Segoe UI") {
            CreateFontW(
                -Int32(24 * scale), 0, 0, 0, 600, 0, 0, 0, DWORD(DEFAULT_CHARSET),
                DWORD(OUT_DEFAULT_PRECIS), DWORD(CLIP_DEFAULT_PRECIS), DWORD(CLEARTYPE_QUALITY), DWORD(DEFAULT_PITCH),
                $0)
        }
        let previous = SelectObject(dc, font)
        defer {
            SelectObject(dc, previous)
            DeleteObject(font)
        }
        var text = RECT(
            left: Int32(20 * scale), top: Int32(14 * scale), right: area.right - Int32(20 * scale),
            bottom: Int32(52 * scale))
        _ = withWideString("☀  \(state.level.percent)%") {
            DrawTextW(dc, $0, -1, &text, UINT(DT_LEFT | DT_VCENTER | DT_SINGLELINE))
        }
        var bar = RECT(
            left: Int32(20 * scale), top: Int32(64 * scale), right: area.right - Int32(20 * scale),
            bottom: Int32(72 * scale))
        fill(bar, 0x0040_3A3A)
        bar.right = bar.left + (bar.right - bar.left) * Int32(state.level.percent) / 100
        fill(bar, 0x005A_B2F5)
    }

    private func queueHardwareMaximum() {
        guard !exiting, let id = (try? Settings())?.targetID else { return }
        let now = GetTickCount64()
        guard lastHardwareCheck == 0 || now - lastHardwareCheck >= 10000 else { return }
        let gate = hardwareGate
        guard
            gate.busy.withLock({
                if $0 { return false }
                $0 = true
                return true
            })
        else { return }
        lastHardwareCheck = now
        do {
            _ = try NativeThread(name: "BrightnessCtl hardware brightness") {
                defer { gate.busy.withLock { $0 = false } }
                do { _ = try ensureHardwareMaximum(displayID: id) } catch { Diagnostics.write("hardware: \(error)") }
            }
        } catch {
            gate.busy.withLock { $0 = false }
            Diagnostics.write("hardware thread: \(error)")
        }
    }

    private func handleRawInput(_ handle: HRAWINPUT?) {
        guard let handle else { return }
        var size: UINT = 0
        let headerSize = UINT(MemoryLayout<RAWINPUTHEADER>.size)
        guard GetRawInputData(handle, UINT(RID_INPUT), nil, &size, headerSize) == 0, size > headerSize + 8,
            size <= 65536
        else { return }
        let buffer = UnsafeMutableRawPointer.allocate(byteCount: Int(size), alignment: MemoryLayout<RAWINPUT>.alignment)
        defer { buffer.deallocate() }
        guard GetRawInputData(handle, UINT(RID_INPUT), buffer, &size, headerSize) == size else { return }
        let header = buffer.load(as: RAWINPUTHEADER.self)
        guard header.dwType == DWORD(RIM_TYPEHID) else { return }
        let reportSize = Int(buffer.advanced(by: Int(headerSize)).load(as: DWORD.self))
        let count = Int(buffer.advanced(by: Int(headerSize) + 4).load(as: DWORD.self))
        guard reportSize > 0, count > 0, count <= 1024, reportSize <= (Int(size) - Int(headerSize) - 8) / count else {
            return
        }
        var preparsedSize: UINT = 0
        guard GetRawInputDeviceInfoW(header.hDevice, UINT(RIDI_PREPARSEDDATA), nil, &preparsedSize) != UINT.max,
            preparsedSize > 0, preparsedSize <= 65536
        else { return }
        let preparsed = UnsafeMutableRawPointer.allocate(byteCount: Int(preparsedSize), alignment: 8)
        defer { preparsed.deallocate() }
        guard GetRawInputDeviceInfoW(header.hDevice, UINT(RIDI_PREPARSEDDATA), preparsed, &preparsedSize) != UINT.max
        else { return }
        for index in 0..<count {
            var usages = Array(repeating: USAGE(0), count: 64)
            var usageCount = ULONG(usages.count)
            let status = HidP_GetUsages(
                HidP_Input, 0x0C, 0, &usages, &usageCount, OpaquePointer(preparsed),
                buffer.advanced(by: Int(headerSize) + 8 + index * reportSize).assumingMemoryBound(to: CChar.self),
                ULONG(reportSize))
            if status >= 0 {
                for usage in usages.prefix(min(Int(usageCount), usages.count)) {
                    if usage == 0x6F { queue(1) } else if usage == 0x70 { queue(-1) }
                }
            }
        }
    }

    func shutdown() {
        guard !exiting else { return }
        exiting = true
        systemIndicator = nil
        keyboard?.stop()
        keyboard = nil
        if let window {
            KillTimer(window, 1)
            KillTimer(window, 2)
            for id in 1...4 { UnregisterHotKey(window, Int32(id)) }
        }
        stopDisplay(controller)
        Shell_NotifyIconW(DWORD(NIM_DELETE), &tray)
        if let osd { DestroyWindow(osd) }
        Diagnostics.write("exit: output restored")
    }

    deinit { shutdown() }
}
private func parseHotkey(_ text: String) -> (modifiers: UINT, key: UINT)? {
    var modifiers: UINT = 0
    var key: UINT = 0
    for part in text.lowercased().split(separator: "+") {
        let part = part.trimmingWhitespace()
        switch part {
        case "ctrl", "control": modifiers |= UINT(MOD_CONTROL)
        case "alt": modifiers |= UINT(MOD_ALT)
        case "shift": modifiers |= UINT(MOD_SHIFT)
        case "win": modifiers |= UINT(MOD_WIN)
        default:
            let named: [String: UINT] = [
                "up": 38, "down": 40, "left": 37, "right": 39, "pageup": 33, "pagedown": 34,
                "home": 36, "end": 35, "space": 32, "oemplus": 187, "oemminus": 189,
            ]
            if let value = named[part] {
                key = value
            } else if part.hasPrefix("f"), let number = UInt32(part.dropFirst()), (1...24).contains(number) {
                key = 111 + number
            } else if part.count == 1, let scalar = part.uppercased().unicodeScalars.first,
                (48...90).contains(scalar.value)
            {
                key = scalar.value
            } else {
                return nil
            }
        }
    }
    return key == 0 ? nil : (modifiers, key)
}
