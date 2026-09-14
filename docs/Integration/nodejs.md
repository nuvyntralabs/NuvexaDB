# Node.js

`@nuventra/nuvexadb-node` loads the C ABI with koffi. `NUVEXA_NATIVE_LIB` is **required**.

## 1. Download

From [NuvexaDB v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1):

| Your machine | Node zip | Native zip | Library file |
| --- | --- | --- | --- |
| macOS Apple Silicon | `NuvexaDB-Node-osx-arm64.zip` | `NuvexaDB-Native-osx-arm64.zip` | `libnuvexa.dylib` |
| Linux x64 | `NuvexaDB-Node-linux-x64.zip` | `NuvexaDB-Native-linux-x64.zip` | `libnuvexa.so` |
| Windows x64 | `NuvexaDB-Node-win-x64.zip` | `NuvexaDB-Native-win-x64.zip` | `nuvexa.dll` |

Inside the Node zip: `nuventra-nuvexadb-node-1.0.1.tgz` (npm pack of `@nuventra/nuvexadb-node`).

This is the **desktop Node** SDK. For React Native apps see [react-native.md](react-native.md).

## 2. Empty project

```bash
mkdir acme-store && cd acme-store
npm init -y
```

Use `"type": "module"` in `package.json` (the SDK is ESM).

## 3. Add the downloaded package

```bash
npm install /path/to/nuventra-nuvexadb-node-1.0.1.tgz
export NUVEXA_NATIVE_LIB="/absolute/path/to/libnuvexa.dylib"
```

Windows:

```powershell
$env:NUVEXA_NATIVE_LIB = "C:\path\to\nuvexa.dll"
```

## 4. Create, open, close

```js
import { NuvexaDatabase } from "@nuventra/nuvexadb-node";

const path = "app.nvx";
const db = await NuvexaDatabase.create(path, key); // omit key / pass null for plaintext
await db.close();

if ((await NuvexaDatabase.isEncrypted(path)) && !key) {
  throw new Error("Encrypted .nvx — pass the key");
}
const opened = await NuvexaDatabase.open(path, key);
await opened.close();
```

## 5. Delete the database

```js
import { unlink } from "node:fs/promises";

await db.close();
await unlink(path);
await unlink(path + "-wal").catch(() => {});
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

Read the key from the environment or a secret manager. Do not commit it. `1234` is a local demo only.

```js
await db.changeEncryptionKey(currentKey, nextKey);
```

See [Encryption notes](README.md#encrypted-databases--choose-a-password).
