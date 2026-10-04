// SPDX-License-Identifier: MIT

import BrightnessCore
import Foundation

/// Portable user settings. Hardware identities remain only in the local file.
struct Settings: Sendable {
    static let directory = URL(
        fileURLWithPath: ProcessInfo.processInfo.environment["LOCALAPPDATA"]
            ?? URL.homeDirectory.appendingPathComponent("AppData/Local").path
    ).appendingPathComponent("BrightnessCtl")
    var targetID: String?
    var legacyTarget: String?
    var step = 5
    var grabFunctionKeys = false
    var interceptInjectedKeys = false
    var restoreOnResume = true
    var hotkeys = ["Ctrl+Alt+Up", "Ctrl+Alt+Down", "Ctrl+Alt+PageUp", "Ctrl+Alt+PageDown"]
    var backend = "auto"
    var brightness: BrightnessLevel

    init() throws {
        try FileManager.default.createDirectory(at: Self.directory, withIntermediateDirectories: true)
        let configuration = Self.directory.appendingPathComponent("config.ini")
        if !FileManager.default.fileExists(atPath: configuration.path) {
            let defaults = [
                "# BrightnessCtl: one physical SDR display. Use CLI list/select.",
                "step=5",
                "up=Ctrl+Alt+Up",
                "down=Ctrl+Alt+Down",
                "max=Ctrl+Alt+PageUp",
                "min=Ctrl+Alt+PageDown",
                "grabF1F2=0",
                "interceptInjectedKeys=0",
                "restoreOnResume=1",
                "backend=auto",
                "targetDisplay=",
                "",
            ].joined(separator: "\r\n")
            try Data(defaults.utf8).write(to: configuration, options: .atomic)
        }
        let brightnessFile = Self.directory.appendingPathComponent("software.txt")
        let saved =
            FileManager.default.fileExists(atPath: brightnessFile.path)
            ? try String(contentsOf: brightnessFile, encoding: .utf8) : ""
        brightness = try BrightnessLevel(
            max(0, min(100, Int(saved.trimmingCharacters(in: .whitespacesAndNewlines)) ?? 100)))
        let content = try String(contentsOf: configuration, encoding: .utf8)
        for line in content.components(separatedBy: .newlines) {
            let parts = line.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
            guard parts.count == 2 else { continue }
            let key = parts[0].trimmingCharacters(in: .whitespaces).lowercased()
            let value = parts[1].trimmingCharacters(in: .whitespaces)
            switch key {
            case "targetdisplay": targetID = value.isEmpty ? nil : value
            case "targetoutput": legacyTarget = value.isEmpty ? nil : value
            case "step": step = max(1, min(25, Int(value) ?? 5))
            case "grabf1f2": grabFunctionKeys = value == "1" || value.lowercased() == "true"
            case "interceptinjectedkeys": interceptInjectedKeys = value == "1" || value.lowercased() == "true"
            case "restoreonresume": restoreOnResume = value == "1" || value.lowercased() == "true"
            case "up": hotkeys[0] = value
            case "down": hotkeys[1] = value
            case "max": hotkeys[2] = value
            case "min": hotkeys[3] = value
            case "backend":
                backend = ["auto", "amd", "native"].contains(value.lowercased()) ? value.lowercased() : "auto"
            default: break
            }
        }
    }

    func saveBrightness(_ level: BrightnessLevel) throws {
        try Data("\(level.percent)\r\n".utf8).write(
            to: Self.directory.appendingPathComponent("software.txt"), options: .atomic)
    }

    mutating func select(_ id: String) throws {
        var content = try String(contentsOf: Self.directory.appendingPathComponent("config.ini"), encoding: .utf8)
        content = content.replacingOccurrences(of: "\r\n", with: "\n").components(separatedBy: "\n")
            .filter { line in
                line.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false).first?
                    .trimmingCharacters(in: .whitespaces).lowercased() != "targetdisplay"
            }.joined(separator: "\r\n").trimmingCharacters(in: .newlines)
        content += "\r\ntargetDisplay=\(id)\r\n"
        try Data(content.utf8).write(to: Self.directory.appendingPathComponent("config.ini"), options: .atomic)
        targetID = id
    }
}
