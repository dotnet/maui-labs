// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "CometSwiftUIShim",
    platforms: [.iOS(.v16)],
    products: [
        .library(name: "CometSwiftUIShim", type: .dynamic, targets: ["CometSwiftUIShim"]),
    ],
    targets: [
        .target(name: "CometSwiftUIShim", path: "Sources/CometSwiftUIShim"),
    ]
)
