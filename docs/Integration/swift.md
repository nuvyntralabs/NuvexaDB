# Swift (macOS and iOS)

Swift package over the C ABI.

## 1. Download

From [NuvexaDB v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1):

### macOS

| Zip | Inner file you need |
| --- | --- |
| **`NuvexaDB-Swift-osx-arm64.zip`** | `nuvexadb-swift-1.0.1.tgz` (Package.swift + Sources) |
| **`NuvexaDB-Native-osx-arm64.zip`** | `libnuvexa.dylib` |

Intel Mac: use `NuvexaDB-Native-osx-x64.zip` (`libnuvexa.dylib`). The Swift sources are the same.

### iOS

| Zip | Inner file you need |
| --- | --- |
| **`NuvexaDB-Native-iOS.zip`** | `Nuvexa.xcframework` and `nuvexa.h` |
| **`NuvexaDB-Swift-osx-arm64.zip`** | Same Swift sources (`nuvexadb-swift-1.0.1.tgz`) |

On iOS the process links the xcframework; you do not set `NUVEXA_NATIVE_LIB` at runtime.

## 2. Empty project

```bash
mkdir AcmeStore && cd AcmeStore
swift package init --type executable --name AcmeStore
```

Xcode: **File → New → Project → App** (macOS or iOS), then add a local package.

## 3. Add the downloaded package

Unpack `nuvexadb-swift-1.0.1.tgz`. In `Package.swift`:

```swift
dependencies: [
    .package(path: "../nuvexadb-swift")
],
targets: [
    .executableTarget(
        name: "AcmeStore",
        dependencies: [
            .product(name: "NuvexaDB", package: "NuvexaDB")
        ]
    )
]
```

macOS — folder that contains `libnuvexa.dylib`:

```bash
export NUVEXA_NATIVE_DIR="/absolute/path/to/native-unzip"
```

iOS — add `Nuvexa.xcframework` to the app target (**Frameworks, Libraries, and Embedded Content**, Embed & Sign).

## 4. Create, open, close

```swift
import NuvexaDB

let path = "app.nvx"
// create writes format 2. Format 1 files still open.
let db = try NuvexaDatabase.create(path, key: key) // key: nil for plaintext
// ...
try db.close()

if try NuvexaDatabase.isEncrypted(path) {
    // require key
}
let opened = try NuvexaDatabase.open(path, key: key)
try opened.close()
```

`deinit` also closes the handle.

## 5. Delete the database

```swift
try db.close()
try FileManager.default.removeItem(atPath: path)
try? FileManager.default.removeItem(atPath: path + "-wal")
```

On iOS use a URL under Application Support or Documents, not the bundle.

## 6. Collections

```swift
_ = try db.insert(collection: "users", json: #"{"name":"Ada","age":36}"#)
print(try db.listCollections())
try db.renameCollection(from: "users", to: "people")
try db.dropCollection("people")
```

## 7. Documents

```swift
let id = try db.insert(collection: "users", json: #"{"name":"Ada","age":36}"#)
_ = try db.insertMany(collection: "users", jsonArray: #"[{"name":"Grace","age":85}]"#)
let ada = try db.findById(collection: "users", id: id)
try db.replace(collection: "users", json: #"{"_id":"\#(id)","name":"Ada Lovelace","age":36}"#)
_ = try db.deleteById(collection: "users", id: id)
let rows = try db.execute(#"db.users.find({ age: { $gte: 21 } }).limit(20)"#)
```

## 8. Password for an encrypted file

Store the key in the **Keychain**. Do not compile a default password into the app. Pass the same string to `create` and `open`.

```swift
try db.changeEncryptionKey(currentKey: current, nextKey: next)
```

See [Encryption notes](README.md#encrypted-databases--choose-a-password).
