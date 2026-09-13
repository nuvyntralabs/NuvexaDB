# NuvexaDB Swift package

SPM library `NuvexaDB` (macOS 13+, iOS 16+) over `nuvexa.h`. Set `NUVEXA_NATIVE_DIR` to the folder with `libnuvexa.dylib`.

## Integration

Do not publish to a Swift registry from this clone. Path package or CI `NuvexaDB-Swift-osx-arm64` plus `NuvexaDB-Native-osx-arm64`.

### Package reference

```swift
.package(path: "../NuvexaDB/bindings/swift")
// product: NuvexaDB
```

```bash
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_DIR="$(pwd)/artifacts/native/osx-arm64"
```

### Create DB, collection, CRUD, queries

```swift
let db = try NuvexaDatabase.create("app.nvx", key: "sample-key")
let id = try db.insert(collection: "users", json: #"{"name":"Ada","age":36,"status":"active","address":{"city":"London"}}"#)
_ = try db.findById(collection: "users", id: id)
try db.replace(collection: "users", json: /* JSON with _id */)
_ = try db.deleteById(collection: "users", id: scratchId)
_ = try db.execute("db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)")
_ = try db.execute(#"db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })"#)
_ = try db.execute(#"db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })"#)
try db.close()
```

First insert creates `users`. Failures throw `NuvexaError` (`.encryption` without a key).

## In-repo sample

[Examples/NuvexaSwiftSample](Examples/NuvexaSwiftSample)

```bash
cd bindings/swift
swift test
swift run NuvexaSwiftSample
```

See [docs/bindings.md](../../docs/bindings.md).
