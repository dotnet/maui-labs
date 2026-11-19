// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "EssentialsAINative",
    platforms: [.macOS("26.0")],
    targets: [
        .target(
            name: "EssentialsAI",
            path: "EssentialsAI",
            exclude: ["EssentialsAI.xcodeproj"]
        ),
        .testTarget(name: "EssentialsAITests", dependencies: ["EssentialsAI"])
    ],
    swiftLanguageModes: [.v5]
)
