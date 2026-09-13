# NuvexaDB JVM SDK

Kotlin-first API. Java calls the same types (`NuvexaDatabase.create`, `@JvmOverloads`). Samples live in this library project, same idea as `samples/Console` for the .NET library.

| Sample | Path |
| --- | --- |
| Java | [src/sampleJava/.../JavaSample.java](src/sampleJava/java/nuventra/nuvexadb/sample/JavaSample.java) |
| Kotlin | [src/sampleKotlin/.../KotlinSample.kt](src/sampleKotlin/kotlin/nuventra/nuvexadb/sample/KotlinSample.kt) |

```bash
# from the NuvexaDB repo root
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_LIB="$(pwd)/artifacts/native/osx-arm64/libnuvexa.dylib"
cd bindings/jvm
gradle test
gradle runJavaSample
gradle runKotlinSample
```

See [docs/bindings.md](../../docs/bindings.md).
