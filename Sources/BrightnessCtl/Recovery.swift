// SPDX-License-Identifier: MIT

import BrightnessCore
import Foundation
import WinSDK

let instanceMutex = "Local\\BrightnessCtl.SingleInstance"
let controlWindowTitle = "BrightnessCtl.Software.v2"
let brightnessMessage: UINT = 0x8001
let keyStepMessage: UINT = 0x8002
struct InstanceLock: ~Copyable {
    let handle: OwnedHandle
    let acquired: Bool
    init(timeout: DWORD) throws {
        handle = try OwnedHandle(withWideString(instanceMutex) { CreateMutexW(nil, false, $0) })
        let result = WaitForSingleObject(handle.raw, timeout)
        acquired = result == DWORD(WAIT_OBJECT_0) || result == 0x80  // WAIT_ABANDONED_0
    }
    deinit { if acquired { ReleaseMutex(handle.raw) } }
}
func processStartTicks(_ process: HANDLE) throws(WindowsError) -> UInt64 {
    var created = FILETIME()
    var exited = FILETIME()
    var kernel = FILETIME()
    var user = FILETIME()
    guard GetProcessTimes(process, &created, &exited, &kernel, &user) else {
        throw .api("GetProcessTimes", GetLastError())
    }
    return (UInt64(created.dwHighDateTime) << 32 | UInt64(created.dwLowDateTime)) + 504_911_232_000_000_000
}
func executablePath() -> String {
    var buffer = Array(repeating: WCHAR(0), count: 32768)
    let count = GetModuleFileNameW(nil, &buffer, DWORD(buffer.count))
    return String(decoding: buffer.prefix(Int(count)), as: UTF16.self)
}
struct ColorLease: Codable, Sendable, Equatable {
    var version = 1
    let owner: UInt32
    let started: UInt64
    let displayID: String
    let backend: String
    let amdID: String?
    let brightness: Int?
    let contrast: Int?
    let gamma: [UInt16]?

    static let path = Settings.directory.appendingPathComponent("scanout-lease.json")
    static func read() throws -> Self? {
        guard FileManager.default.fileExists(atPath: path.path) else { return nil }
        let data = try Data(contentsOf: path)
        guard data.count < 32768 else { throw WindowsError.unsupported("Invalid recovery-state size.") }
        let lease = try JSONDecoder().decode(Self.self, from: data)
        guard lease.version == 1, lease.displayID.count < 4096, ["amd", "native"].contains(lease.backend) else {
            throw WindowsError.unsupported("Unsupported output recovery state.")
        }
        return lease
    }
    func write() throws { try JSONEncoder().encode(self).write(to: Self.path, options: .atomic) }
    static func remove() throws {
        if FileManager.default.fileExists(atPath: path.path) { try FileManager.default.removeItem(at: path) }
    }
}
/// Called under the instance mutex before a new backend captures its baseline.
@discardableResult
func recoverOutput(owner: UInt32? = nil, started: UInt64? = nil) throws -> Bool {
    if let lease = try ColorLease.read() {
        if let owner, let started, owner != lease.owner || started != lease.started { return false }
        let outputs = try discoverDisplays()
        guard
            let output = outputs.first(where: { $0.id == lease.displayID && $0.isPhysical && !$0.isHDR && !$0.isCloned }
            )
        else { return false }
        if lease.backend == "amd" {
            let control = try AMDControl()
            guard let id = lease.amdID,
                let amd = try control.enumerate().first(where: {
                    $0.legacyID == id && $0.device.caseInsensitiveCompare(output.device) == .orderedSame
                }),
                let brightness = lease.brightness, let contrast = lease.contrast
            else { throw WindowsError.unsupported("Invalid AMD recovery state.") }
            try control.set(amd, brightness: brightness, contrast: contrast)
        } else {
            guard let gamma = lease.gamma else { throw WindowsError.unsupported("Missing native recovery ramp.") }
            let ramp = try GammaRamp(samples: gamma)
            let session = try NativeGammaSession(output: output, emulateOwnership: true)
            try session.restoreSaved(ramp)
        }
        try ColorLease.remove()
        Diagnostics.write("recovery: original output color state restored")
    }
    // Preserve the baseline when migrating from the public 0.1.x versions.
    let legacy = Settings.directory.appendingPathComponent("output-color-lease.txt")
    guard FileManager.default.fileExists(atPath: legacy.path) else { return true }
    let lines = try String(contentsOf: legacy, encoding: .utf8).split(whereSeparator: \.isNewline).map(String.init)
    guard lines.count == 7, let brightness = Int(lines[3]), let contrast = Int(lines[4]) else {
        throw WindowsError.unsupported("Invalid legacy recovery state.")
    }
    if let owner, let started, lines[0] != String(owner) || lines[1] != String(started) { return false }
    let control = try AMDControl()
    guard let output = try control.enumerate().first(where: { $0.legacyID == lines[2] }) else { return false }
    guard
        try discoverDisplays().contains(where: {
            $0.device.caseInsensitiveCompare(output.device) == .orderedSame && $0.isPhysical && !$0.isHDR
                && !$0.isCloned
        })
    else { return false }
    try control.set(output, brightness: brightness, contrast: contrast)
    try FileManager.default.removeItem(at: legacy)
    return true
}
func runWatchdog(owner: UInt32, started: UInt64) throws {
    if let handle = OpenProcess(DWORD(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION), false, owner) {
        let process = try OwnedHandle(handle)
        guard try processStartTicks(process.raw) == started else { return }
        WaitForSingleObject(process.raw, DWORD(INFINITE))
    }
    let lock = try InstanceLock(timeout: 1000)
    guard lock.acquired else { return }
    try recoverOutput(owner: owner, started: started)
}
func startWatchdog() throws {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: executablePath())
    process.arguments = [
        "--watchdog", String(GetCurrentProcessId()), String(try processStartTicks(GetCurrentProcess())),
    ]
    process.standardInput = FileHandle.nullDevice
    process.standardOutput = FileHandle.nullDevice
    process.standardError = FileHandle.nullDevice
    try process.run()
}
