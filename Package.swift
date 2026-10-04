// swift-tools-version: 6.4
// SPDX-License-Identifier: MIT
import PackageDescription

var targets: [Target] = [
    .target(name: "BrightnessCore"),
    .testTarget(name: "BrightnessCoreTests", dependencies: ["BrightnessCore"], path: "tests/BrightnessCoreTests"),
]
var products: [Product] = [.library(name: "BrightnessCore", targets: ["BrightnessCore"])]
#if os(Windows)
    targets += [
        .systemLibrary(name: "WindowsDisplayABI", path: "Sources/WindowsDisplayABI"),
        .executableTarget(
            name: "BrightnessCtl", dependencies: ["BrightnessCore", "WindowsDisplayABI"],
            linkerSettings: [
                .linkedLibrary("user32"), .linkedLibrary("gdi32"),
                .linkedLibrary("shell32"), .linkedLibrary("dxva2"), .linkedLibrary("hid"),
                .linkedLibrary("ole32"), .linkedLibrary("oleaut32"),
            ]),
    ]
    products.append(.executable(name: "BrightnessCtl", targets: ["BrightnessCtl"]))
#endif
let package = Package(
    name: "BrightnessCtl", platforms: [.macOS(.v26)], products: products, targets: targets,
    swiftLanguageModes: [.v6])
