// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "ContextSnapMac",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "ContextSnap", targets: ["ContextSnap"]),
    ],
    targets: [
        // Platform-neutral logic shared in behavior with the Windows app (see ../spec).
        .target(name: "ContextSnapCore"),
        // Menu-bar app: Accessibility, ScreenCaptureKit, Vision, hotkey, pasteboard.
        .executableTarget(name: "ContextSnap", dependencies: ["ContextSnapCore"]),
        .testTarget(name: "ContextSnapCoreTests", dependencies: ["ContextSnapCore"]),
    ]
)
