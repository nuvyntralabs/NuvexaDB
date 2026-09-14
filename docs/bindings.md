# Language bindings

New host? Start with the [platform integration guides](Integration/README.md) (which Release zip to download, empty project, CRUD, encryption password).

How the engine is shared (Native AOT, ABI, publish path): [architecture.md](architecture.md).

One engine (`Nuventra.NuvexaDB`), one C ABI (`Nuventra.NuvexaDB.Native`), thin SDKs:

| Host | SDK | Native library |
| --- | --- | --- |
| Java and Kotlin (desktop) | [bindings/jvm](../bindings/jvm) | `libnuvexa.dylib` / `.so` / `nuvexa.dll` |
| Android Kotlin / Java | [bindings/android](../bindings/android) AAR | `jniLibs/**/libnuvexa.so` |
| Swift (macOS, then iOS) | [bindings/swift](../bindings/swift) | same dylib, later `Nuvexa.xcframework` |
| Flutter / Dart | [bindings/flutter](../bindings/flutter) | `dart:ffi` → same library |
| React Native | [bindings/react-native](../bindings/react-native) | iOS `nuvexa.h`, Android Kotlin SDK, Node tests via koffi |
| Python | [bindings/python](../bindings/python) | ctypes → same library |
| Node.js | [bindings/node](../bindings/node) | koffi → same library |
| Go | [bindings/go](../bindings/go) | cgo → same library |
| C++ | [bindings/cpp](../bindings/cpp) | `nuvexa.hpp` + `-lnuvexa` |

Do not port pages, WAL, or Argon2. Documents and NQL stay JSON strings.

## C ABI (v2)

Header: [nuvexa.h](../src/Nuventra.NuvexaDB.Native/include/nuvexa.h). `nuvexa_abi_version()` returns **2**.

```text
create / open / close / is_encrypted
insert / insert_many / replace / delete_by_id / find_by_id
execute(nql) → JSON array
ensure_index / list_indexes / drop_index
list_collections / drop_collection / rename_collection
count(filter_json)
checkpoint / backup / compact / restore / stats
change_encryption_key
begin_transaction / commit / rollback
fs_upload / fs_download / fs_metadata   (file paths, not byte buffers)
last_error / free
```

Status: `0` ok, `1` error, `2` encryption, `3` integrity, `4` not found. Free every `char**` with `nuvexa_free`.

Native AOT enables `JsonSerializerIsReflectionEnabledByDefault` so NQL and index JSON keep working. Do not turn that off.

Publish (current desktop RID):

```bash
src/Nuventra.NuvexaDB.Native/publish.sh
```

Desktop CI RIDs: `osx-arm64`, `osx-x64`, `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`. Android CI publishes `linux-bionic-arm64` as `NuvexaDB-Native-android-arm64` (NDK on `PATH`; Native AOT cannot target `android-arm64`). iOS CI publishes `ios-arm64` + `iossimulator-arm64` (`PublishAotUsingRuntimePack`) and packs `Nuvexa.xcframework` as `NuvexaDB-Native-iOS`.

## Kotlin / Java

```kotlin
NuvexaDatabase.create("app.nvx", "correct-horse").use { db ->
    db.insert("users", """{"name":"Ada","age":36}""")
    db.execute("db.users.find({ age: { \$gte: 21 } }).limit(20)")
    db.listCollections()
    db.count("users", """{"age":{"$gte":21}}""")
}
```

Java calls the same types (`NuvexaDatabase.create(path, key)`). Set `NUVEXA_NATIVE_LIB` to the published library file.

## Swift

```swift
let db = try NuvexaDatabase.create("app.nvx", key: "correct-horse")
_ = try db.insert(collection: "users", json: #"{"name":"Ada","age":36}"#)
_ = try db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)")
try db.close()
```

Set `NUVEXA_NATIVE_DIR` to the folder that contains `libnuvexa.dylib`.

## Flutter / Dart

```dart
final db = NuvexaDatabase.create('app.nvx', key: 'correct-horse');
db.insert('users', '{"name":"Ada","age":36}');
db.execute('db.users.find({ age: { \$gte: 21 } }).limit(20)');
db.close();
```

Set `NUVEXA_NATIVE_LIB`. iOS uses `DynamicLibrary.process()` after linking the xcframework.

## React Native

```js
const db = await NuvexaDatabase.create("app.nvx", "correct-horse");
await db.insert("users", JSON.stringify({ name: "Ada", age: 36 }));
await db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)");
await db.close();
```

JS is async so it matches `NativeModules`. Node tests load the C ABI with koffi (`NUVEXA_NATIVE_LIB`).

## Python

```python
from nuvexadb import NuvexaDatabase

with NuvexaDatabase.create("app.nvx", "correct-horse") as db:
    db.insert("users", '{"name":"Ada","age":36}')
    db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)")
```

## Node.js

```js
import { NuvexaDatabase } from "@nuventra/nuvexadb-node";

const db = await NuvexaDatabase.create("app.nvx", "correct-horse");
await db.insert("users", JSON.stringify({ name: "Ada", age: 36 }));
```

## Go

```go
db, err := nuvexa.Create("app.nvx", "correct-horse")
_, err = db.Insert("users", `{"name":"Ada","age":36}`)
rows, err := db.Execute(`db.users.find({ age: { $gte: 21 } }).limit(20)`)
```

Set `CGO_LDFLAGS=-L$NUVEXA_NATIVE_DIR -lnuvexa` and the platform library path (`DYLD_LIBRARY_PATH` / `LD_LIBRARY_PATH`).

## C++

```cpp
auto db = nuvexa::database::create("app.nvx", "correct-horse");
db.insert("users", R"({"name":"Ada","age":36})");
auto rows = db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)");
```

## Fixtures

Every SDK must pass [tests/interop/cases.json](../tests/interop/cases.json). ABI v2 catalog / transaction / GridFS cases live in `ExtendedAbiTests` and each SDK’s extended test. See [tests/interop/README.md](../tests/interop/README.md).

## Samples

Language samples live inside each SDK project (same idea as `samples/Console` for .NET):

- [bindings/jvm](../bindings/jvm) — `src/sampleJava`, `src/sampleKotlin`
- [bindings/android](../bindings/android) — `sample/MainActivity.kt`
- [bindings/swift](../bindings/swift) — `Examples/NuvexaSwiftSample`
- [bindings/flutter](../bindings/flutter) — `examples/sample.dart`
- [bindings/react-native](../bindings/react-native) — `examples/sample.mjs`
- [bindings/python](../bindings/python) — `examples/sample.py`
- [bindings/node](../bindings/node) — `examples/sample.mjs`
- [bindings/go](../bindings/go) — `examples/sample`
- [bindings/cpp](../bindings/cpp) — `examples/sample.cpp`

Publishing Maven, Swift Package, npm, pub.dev, PyPI, or NuGet from a local clone is not allowed. CI tests each SDK against the published C ABI and uploads GitHub artifacts (`NuvexaDB-Java-<rid>`, `NuvexaDB-Python-<rid>`, …). Host publish stays commented out in `.github/workflows/ci.yml`.
