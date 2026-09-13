# Node.js library

Standalone Node SDK over `nuvexa.h` (koffi). ABI v2.

| Piece | Path |
| --- | --- |
| Library | `src/` (`package.json`) |
| Tests | `test/interop.test.mjs` |
| Sample | [examples/sample.mjs](examples/sample.mjs) |
| This file | `README.md` |

```bash
# from the NuvexaDB repo root
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_LIB="$(pwd)/artifacts/native/osx-arm64/libnuvexa.dylib"
cd bindings/node
npm install
npm test
npm run sample
```

```js
import { NuvexaDatabase } from "@nuventra/nuvexadb-node";

const db = await NuvexaDatabase.create("app.nvx", "correct-horse");
await db.insert("users", JSON.stringify({ name: "Ada", age: 36 }));
await db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)");
await db.close();
```

Do not `npm publish` from this clone.
