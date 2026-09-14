# Flutter / Dart

`dart:ffi` wrapper. Desktop needs the C ABI next to the app. iOS links `Nuvexa.xcframework`. Android ships `libnuvexa.so` in the APK.

## 1. Download

From [NuvexaDB v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1):

| Host you are running | Flutter zip | Native zip | Library file |
| --- | --- | --- | --- |
| macOS Apple Silicon | `NuvexaDB-Flutter-osx-arm64.zip` | `NuvexaDB-Native-osx-arm64.zip` | `libnuvexa.dylib` |
| Linux x64 | `NuvexaDB-Flutter-linux-x64.zip` | `NuvexaDB-Native-linux-x64.zip` | `libnuvexa.so` |
| Android | same Dart package (`nuvexadb-flutter-1.0.1.tgz` inside any Flutter zip) | use `NuvexaDB-Android.zip` / `NuvexaDB-Native-android-arm64.zip` for `libnuvexa.so` | `libnuvexa.so` in `jniLibs/arm64-v8a/` |
| iOS | same Dart package | **`NuvexaDB-Native-iOS.zip`** | `Nuvexa.xcframework` |

The Flutter zip contains `nuvexadb-flutter-1.0.1.tgz` (`pubspec.yaml` + `lib/`). The Dart sources do not change by RID; the native zip must match the device.

## 2. Empty project

```bash
flutter create acme_store
cd acme_store
```

## 3. Add the downloaded package

Unpack `nuvexadb-flutter-1.0.1.tgz` to a sibling folder. In `pubspec.yaml`:

```yaml
dependencies:
  nuvexadb:
    path: ../nuvexadb-flutter
```

Desktop / tests:

```bash
export NUVEXA_NATIVE_LIB="/absolute/path/to/libnuvexa.dylib"
```

iOS: add `Nuvexa.xcframework` to the Xcode Runner target; the Dart library uses `DynamicLibrary.process()`.

Android: place `libnuvexa.so` at `android/app/src/main/jniLibs/arm64-v8a/libnuvexa.so` (or consume the published AAR). The loader then opens `libnuvexa.so`.

```bash
flutter pub get
```

## 4. Create, open, close

```dart
import 'package:nuvexadb/nuvexadb.dart';

final path = 'app.nvx';
final db = NuvexaDatabase.create(path, key: key);
// ...
db.close();

if (NuvexaDatabase.isEncrypted(path) && key == null) {
  throw StateError('Encrypted .nvx — pass key');
}
final opened = NuvexaDatabase.open(path, key: key);
opened.close();
```

On a phone, use `path_provider` application-documents or support directory, not the project folder.

## 5. Delete the database

```dart
db.close();
File(path).deleteSync();
final wal = File('$path-wal');
if (wal.existsSync()) wal.deleteSync();
```

## 6. Collections

```dart
db.insert('users', '{"name":"Ada","age":36}');
print(db.listCollections());
db.renameCollection('users', 'people');
db.dropCollection('people');
```

## 7. Documents

```dart
final id = db.insert('users', '{"name":"Ada","age":36}');
db.insertMany('users', '[{"name":"Grace","age":85}]');
final ada = db.findById('users', id);
db.replace('users', '{"_id":"$id","name":"Ada Lovelace","age":36}');
db.deleteById('users', id);
final rows = db.execute(r'db.users.find({ age: { $gte: 21 } }).limit(20)');
```

## 8. Password for an encrypted file

On mobile, keep the key in the platform secure store (`flutter_secure_storage` or equivalent). Do not bake `1234` into Dart source.

```dart
db.changeEncryptionKey(currentKey, nextKey);
```

See [Encryption notes](README.md#encrypted-databases--choose-a-password).
