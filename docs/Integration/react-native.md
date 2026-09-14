# React Native

JavaScript API is async (`NativeModules` on device). Node tests load the C ABI with koffi.

## 1. Download

From [NuvexaDB v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1):

| What you are building | Zip | Inner file |
| --- | --- | --- |
| JS package (all hosts) | `NuvexaDB-React-Native-osx-arm64.zip` or `NuvexaDB-React-Native-linux-x64.zip` | `@nuventra/nuvexadb` tarball (`nuventra-nuvexadb-1.0.1.tgz`) |
| Android app | **`NuvexaDB-React-Native-Android.zip`** | Android AAR with `jni/arm64-v8a/libnuvexa.so` |
| iOS app | **`NuvexaDB-React-Native-iOS.zip`** plus **`NuvexaDB-Native-iOS.zip`** | compiled `NuvexaDB.mm` / link **`Nuvexa.xcframework`** |
| Node / Jest on a desktop | same JS tarball + `NuvexaDB-Native-<rid>.zip` | `libnuvexa.dylib` / `.so` / `nuvexa.dll` |

The JS sources are the same in every `NuvexaDB-React-Native-<rid>.zip`. Download one.

## 2. Empty project

```bash
npx @react-native-community/cli init AcmeStore
cd AcmeStore
```

## 3. Add the downloaded package

```bash
npm install /path/to/nuventra-nuvexadb-1.0.1.tgz
```

Wire the native module:

- **Android** — add the AAR from `NuvexaDB-React-Native-Android.zip` to the `android` app (or the binding’s `android/` folder from the tarball) so `libnuvexa.so` is in `jni/arm64-v8a/`.
- **iOS** — `pod install` after adding the podspec from the tarball; embed `Nuvexa.xcframework` from `NuvexaDB-Native-iOS.zip`.

Desktop tests only:

```bash
export NUVEXA_NATIVE_LIB="/absolute/path/to/libnuvexa.dylib"
```

## 4. Create, open, close

```js
import { NuvexaDatabase } from "@nuventra/nuvexadb";

const path = "app.nvx";
// create writes format 2. Format 1 files still open.
const db = await NuvexaDatabase.create(path, key);
await db.close();

if (await NuvexaDatabase.isEncrypted(path) && !key) {
  throw new Error("Encrypted .nvx — pass the key");
}
const opened = await NuvexaDatabase.open(path, key);
await opened.close();
```

On device, `path` must be a writable sandbox file (for example from `react-native-fs` DocumentDirectoryPath).

## 5. Delete the database

```js
await db.close();
await RNFS.unlink(path);
try { await RNFS.unlink(path + "-wal"); } catch { /* no WAL */ }
```

## 6. Collections

```js
await db.insert("users", JSON.stringify({ name: "Ada", age: 36 }));
console.log(await db.listCollections());
await db.renameCollection("users", "people");
await db.dropCollection("people");
```

## 7. Documents

```js
const id = await db.insert("users", JSON.stringify({ name: "Ada", age: 36 }));
await db.insertMany("users", JSON.stringify([{ name: "Grace", age: 85 }]));
const ada = await db.findById("users", id);
await db.replace("users", JSON.stringify({ _id: id, name: "Ada Lovelace", age: 36 }));
await db.deleteById("users", id);
const rows = await db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)");
```

## 8. Password for an encrypted file

Keep the key in Keychain / Keystore (for example `react-native-keychain`). Never ship `1234` in JS.

```js
await db.changeEncryptionKey(currentKey, nextKey);
```

See [Encryption notes](README.md#encrypted-databases--choose-a-password).
