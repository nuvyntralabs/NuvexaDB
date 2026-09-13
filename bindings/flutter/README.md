# NuvexaDB Flutter / Dart

Pub package `nuvexadb` — `dart:ffi` over the Native AOT C ABI.

## Integration

Do not `dart pub publish` from this clone. Path dependency or CI `NuvexaDB-Flutter-<rid>` plus `NuvexaDB-Native-<rid>`.

### Package reference

```yaml
dependencies:
  nuvexadb:
    path: ../NuvexaDB/bindings/flutter
```

Desktop/tests: set `NUVEXA_NATIVE_LIB`. iOS uses `DynamicLibrary.process()` after linking. Android ships `libnuvexa.so` (`arm64-v8a`).

```bash
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_LIB="$(pwd)/artifacts/native/osx-arm64/libnuvexa.dylib"
```

### Create DB, collection, CRUD, queries

```dart
final db = NuvexaDatabase.create('app.nvx', key: 'sample-key');
final id = db.insert('users', '{"name":"Ada","age":36,"status":"active","address":{"city":"London"}}');
db.replace('users', '{"_id":"$id","name":"Ada Lovelace","age":36,"status":"active","address":{"city":"London"}}');
db.ensureIndex('users', ['age']);
db.execute('db.users.find({ age: { \$gte: 21 } }).sort({ name: 1 }).limit(10)');
db.execute('db.users.find({ \$and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })');
db.execute('db.users.find({ \$or: [ { age: { \$lt: 30 } }, { "address.city": "New York" } ] })');
db.close();
```

First insert creates `users`. Opening without a key throws `NuvexaEncryptionException`.

## In-repo sample

[examples/sample.dart](examples/sample.dart)

```bash
cd bindings/flutter
dart test
dart run examples/sample.dart
```

See [docs/bindings.md](../../docs/bindings.md).
