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

CI publishes `NuvexaDB-React-Native-Android` (AAR + Bionic `libnuvexa.so`) and compiles the iOS module (`NuvexaDB-React-Native-iOS`). Golden cases for Apple run on the macOS dylib; the .NET 10 SDK cannot PublishAot `ios-arm64`. Do not publish this package from a local clone.
