// swift-tools-version: 5.9
import PackageDescription

let nativeDir = Context.environment["NUVEXA_NATIVE_DIR"]
var linker: [LinkerSetting] = [.linkedLibrary("nuvexa")]
if let nativeDir, !nativeDir.isEmpty {
    linker.append(contentsOf: [
        .unsafeFlags(["-L\(nativeDir)"]),
        .unsafeFlags(["-Xlinker", "-rpath", "-Xlinker", nativeDir])
    ])
}

let package = Package(
    name: "NuvexaDB",
    platforms: [
        .macOS(.v13),
        .iOS(.v16)
    ],
    products: [
        .library(name: "NuvexaDB", targets: ["NuvexaDB"]),
        .executable(name: "NuvexaSwiftSample", targets: ["NuvexaSwiftSample"])
    ],
    targets: [
        .systemLibrary(name: "CNuvexa", path: "Sources/CNuvexa"),
        .target(
            name: "NuvexaDB",
            dependencies: ["CNuvexa"],
            linkerSettings: linker
        ),
        .executableTarget(
            name: "NuvexaSwiftSample",
            dependencies: ["NuvexaDB"],
            path: "Examples/NuvexaSwiftSample",
            linkerSettings: linker
        ),
        .testTarget(
            name: "NuvexaDBTests",
            dependencies: ["NuvexaDB"]
        )
    ]
)
