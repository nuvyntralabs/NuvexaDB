# NuvexaDB Swift package

macOS first. iOS uses the same overlay plus an xcframework of the static Native AOT library. Sample: [Examples/NuvexaSwiftSample](Examples/NuvexaSwiftSample).

```bash
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_DIR="$(pwd)/artifacts/native/osx-arm64"
cd bindings/swift
swift test
swift run NuvexaSwiftSample
```

iOS xcframework (needs the iOS workload):

```bash
src/Nuventra.NuvexaDB.Native/pack-xcframework.sh
```
