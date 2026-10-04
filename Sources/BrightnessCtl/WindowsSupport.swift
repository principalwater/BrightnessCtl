// SPDX-License-Identifier: MIT

import Foundation
import Synchronization
import WinSDK
import WindowsDisplayABI

/// A checked error returned by a native Windows or display-driver operation.
enum WindowsError: Error, Sendable, CustomStringConvertible {
    case api(String, UInt32)
    case status(String, Int32)
    case unsupported(String)

    var description: String {
        switch self {
        case .api(let operation, let code): "\(operation) failed (Win32 \(code))."
        case .status(let operation, let code): "\(operation) failed (status \(UInt32(bitPattern: code)))."
        case .unsupported(let reason): reason
        }
    }
}
/// Owns one Windows handle. Swift forbids accidental copies and double closes.
struct OwnedHandle: ~Copyable {
    let raw: HANDLE

    init(_ raw: HANDLE?) throws(WindowsError) {
        guard let raw, raw != INVALID_HANDLE_VALUE else { throw .api("Create handle", GetLastError()) }
        self.raw = raw
    }

    deinit { CloseHandle(raw) }
}
/// Keeps a UTF-16 pointer alive only for the duration of a native call.
func withWideString<Result>(_ value: String, _ body: (UnsafePointer<WCHAR>) throws -> Result) rethrows -> Result {
    try (Array(value.utf16) + [0]).withUnsafeBufferPointer { try body($0.baseAddress!) }
}
/// Decodes the bounded, null-terminated UTF-16 fields imported from Windows SDK.
func wideString<Value>(_ value: Value) -> String {
    withUnsafeBytes(of: value) { bytes in
        let words = bytes.bindMemory(to: UInt16.self)
        return String(decoding: words.prefix(while: { $0 != 0 }), as: UTF16.self)
    }
}
func setWideString<Value>(_ value: String, in field: inout Value) {
    withUnsafeMutableBytes(of: &field) { bytes in
        let words = bytes.bindMemory(to: UInt16.self)
        words.initialize(repeating: 0)
        for (index, word) in value.utf16.prefix(max(0, words.count - 1)).enumerated() { words[index] = word }
    }
}
/// A thread-safe message destination without sharing thread-affine UI objects.
struct MessageDestination: Sendable {
    let address: UInt
    init(_ window: HWND) { address = UInt(bitPattern: window) }
    var window: HWND { HWND(bitPattern: address)! }
    func post(_ message: UINT, value: Int = 0, data: Int = 0) -> Bool {
        PostMessageW(window, message, WPARAM(bitPattern: Int64(value)), LPARAM(data))
    }
}
/// Local diagnostics stay in the user installation and may contain native error details.
enum Diagnostics {
    private static let lock = Mutex(())
    static func write(_ message: String) {
        lock.withLock { _ in
            let path = Settings.directory.appendingPathComponent("startup.log")
            try? FileManager.default.createDirectory(at: Settings.directory, withIntermediateDirectories: true)
            if !FileManager.default.fileExists(atPath: path.path) {
                _ = FileManager.default.createFile(atPath: path.path, contents: nil)
            }
            let line = "\(Date.now.formatted(.iso8601))  \(message)\r\n"
            guard let data = line.data(using: .utf8), let handle = try? FileHandle(forWritingTo: path) else { return }
            defer { try? handle.close() }
            _ = try? handle.seekToEnd()
            try? handle.write(contentsOf: data)
        }
    }
}
