# Node.js library

ESM package `@nuventra/nuvexadb-node` (koffi). ABI v2. All methods are **async**.

## Integration

Do not `npm publish` from this clone. Use a path install or CI `NuvexaDB-Node-<rid>` plus `NuvexaDB-Native-<rid>`.

### Package reference

`NUVEXA_NATIVE_LIB` is **required**.

```bash
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_LIB="$(pwd)/artifacts/native/osx-arm64/libnuvexa.dylib"
npm install ../NuvexaDB/bindings/node
```

```js
import { NuvexaDatabase } from "@nuventra/nuvexadb-node";
```

### Create the database and a collection

```js
// create writes format 2. Format 1 files still open.
const db = await NuvexaDatabase.create("app.nvx", "sample-key");
const adaId = await db.insert("users", JSON.stringify({ name: "Ada", age: 36, status: "active", address: { city: "London" } }));
console.log(await db.listCollections());
```

### CRUD

`insert` / `insertMany` → `findById` → `replace` (JSON includes `_id`) → `deleteById`.

### Complex queries

```js
await db.ensureIndex("users", "age");
await db.execute("db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)");
await db.execute('db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })');
await db.execute('db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })');
await db.close();
```

Opening without a key rejects with `NuvexaEncryptionException`.

## In-repo sample

[examples/sample.mjs](examples/sample.mjs)

```bash
cd bindings/node
npm install && npm test && npm run sample
```

See [docs/bindings.md](../../docs/bindings.md).
