# NuvexaDB React Native

npm package `@nuventra/nuvexadb`. On device, iOS calls `nuvexa.h` and Android reuses the Kotlin SDK. Node tests load the desktop library with koffi. Methods are **async**. `react-native >= 0.73`.

## Integration

Do not `npm publish` from this clone. CI: `NuvexaDB-React-Native-<rid>`, `NuvexaDB-React-Native-Android`, `NuvexaDB-React-Native-iOS`.

### Package reference

```bash
npm install ./nuventra-nuvexadb-1.0.0.tgz
export NUVEXA_NATIVE_LIB="$(pwd)/artifacts/native/osx-arm64/libnuvexa.dylib"  # Node tests
```

```js
import { NuvexaDatabase } from "@nuventra/nuvexadb";
```

Android: ship the CI AAR (`jni/arm64-v8a/libnuvexa.so`). iOS: `NuvexaDB.mm` / podspec `nuvexadb` and `-lnuvexa`.

### Create DB, collection, CRUD, queries

```js
// create writes format 2. Format 1 files still open.
const db = await NuvexaDatabase.create("app.nvx", "sample-key");
const id = await db.insert("users", JSON.stringify({ name: "Ada", age: 36, status: "active", address: { city: "London" } }));
await db.replace("users", JSON.stringify({ _id: id, name: "Ada Lovelace", age: 36, status: "active", address: { city: "London" } }));
await db.execute("db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)");
await db.execute('db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })');
await db.execute('db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })');
await db.close();
```

First insert creates `users`. Native reject codes `ENCRYPTION` / `INTEGRITY` / `ERROR`.

## In-repo sample

[examples/sample.mjs](examples/sample.mjs)

```bash
cd bindings/react-native
npm test
node examples/sample.mjs
```

See [docs/bindings.md](../../docs/bindings.md).
