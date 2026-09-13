# NuvexaDB React Native

TypeScript / JS API over the same C ABI. On device, iOS calls `nuvexa.h` and Android reuses the Kotlin SDK. In Node tests, [koffi](https://koffi.dev/) loads the published native library.

```js
import { NuvexaDatabase } from "@nuventra/nuvexadb";

const db = await NuvexaDatabase.create("app.nvx", "correct-horse");
await db.insert("users", JSON.stringify({ name: "Ada", age: 36 }));
const rows = await db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)");
await db.close();
```

Sample: [examples/sample.mjs](examples/sample.mjs).

```bash
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_LIB="$(pwd)/artifacts/native/osx-arm64/libnuvexa.dylib"
cd bindings/react-native
npm test
node examples/sample.mjs
```

Link `libnuvexa` into the iOS app (xcframework) and ship `jniLibs/**/libnuvexa.so` on Android. Do not publish this package from a local clone.
