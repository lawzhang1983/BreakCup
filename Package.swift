// swift-tools-version: 5.10

import PackageDescription

// 本机只装了 Command Line Tools：swift-testing 框架需要显式指路，
// 且 `swift test` 只编译不执行，所以测试编成可执行目标，用 `swift run BreakCupCoreTests` 运行
let testingFrameworks = "/Library/Developer/CommandLineTools/Library/Developer/Frameworks"
let testingLibraries = "/Library/Developer/CommandLineTools/Library/Developer/usr/lib"

let package = Package(
    name: "BreakCup",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "BreakCupCore", targets: ["BreakCupCore"]),
        .executable(name: "BreakCup", targets: ["BreakCup"])
    ],
    targets: [
        .target(name: "BreakCupCore"),
        .executableTarget(
            name: "BreakCup",
            dependencies: ["BreakCupCore"]
        ),
        .executableTarget(
            name: "BreakCupCoreTests",
            dependencies: ["BreakCupCore"],
            path: "Tests/BreakCupCoreTests",
            swiftSettings: [
                .unsafeFlags(["-F", testingFrameworks])
            ],
            linkerSettings: [
                .unsafeFlags([
                    "-F", testingFrameworks,
                    "-Xlinker", "-rpath", "-Xlinker", testingFrameworks,
                    "-Xlinker", "-rpath", "-Xlinker", testingLibraries
                ])
            ]
        )
    ]
)
