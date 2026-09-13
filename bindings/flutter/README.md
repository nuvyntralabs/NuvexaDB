# NuvexaDB Flutter / Dart

`dart:ffi` over the Native AOT C ABI. Same JSON / NQL surface as Kotlin and Swift.

```dart
final db = NuvexaDatabase.create('app.nvx', key: 'correct-horse');
db.insert('users', '{"name":"Ada","age":36}');
final rows = db.execute('db.users.find({ age: { \$gte: 21 } }).limit(20)');
db.close();
```

Sample: [examples/sample.dart](examples/sample.dart).

```bash
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_LIB="$(pwd)/artifacts/native/osx-arm64/libnuvexa.dylib"
cd bindings/flutter
dart test
dart run examples/sample.dart
```

iOS links the xcframework and uses `DynamicLibrary.process()`. Android ships `libnuvexa.so` next to the app. See [docs/bindings.md](../../docs/bindings.md).
