// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Lakhfana",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "Lakhfana", targets: ["Lakhfana"]),
    ],
    targets: [
        .target(
            name: "LakhfanaCore",
            path: "Sources/LakhfanaCore",
            linkerSettings: [.linkedFramework("Carbon")]
        ),
        .executableTarget(
            name: "Lakhfana",
            dependencies: ["LakhfanaCore"],
            path: "Sources/Lakhfana",
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("Carbon"),
                .linkedFramework("ServiceManagement"),
            ]
        ),
        .testTarget(
            name: "LakhfanaCoreTests",
            dependencies: ["LakhfanaCore"],
            path: "Tests/LakhfanaCoreTests"
        ),
    ],
    swiftLanguageVersions: [.v5]
)
