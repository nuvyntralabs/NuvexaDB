# NuvexaDB JVM SDK

Kotlin-first library (`nuventra.nuvexadb`) over the shared Native AOT C ABI. Java calls the same types. A `.nvx` written here is the same file Explorer and the .NET engine open.

## Integration

Host registries are not published from a local clone. Use this project or CI artifact `NuvexaDB-Java-<rid>` plus `NuvexaDB-Native-<rid>`.

### Package / project reference

```kotlin
// settings.gradle.kts
includeBuild("../NuvexaDB/bindings/jvm")
// or implementation(files("nuvexadb-jvm-1.0.0.jar"))
// plus net.java.dev.jna:jna:5.17.0 and org.json:json:20250107
```

`group = nuventra`, `version = 1.0.0`. Set `NUVEXA_NATIVE_LIB` to `libnuvexa.dylib` / `.so` / `nuvexa.dll`.

```bash
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_LIB="$(pwd)/artifacts/native/osx-arm64/libnuvexa.dylib"
```

### Create the database and a collection

The first `insert` / `insertMany` into `"users"` creates that collection.

```kotlin
// create writes format 2. Format 1 files still open.
NuvexaDatabase.create("app.nvx", "sample-key").use { db ->
    db.insert("users", """{"name":"Ada","age":36,"status":"active","address":{"city":"London"}}""")
    println(db.listCollections())
}
```

### CRUD

| Step | API |
| --- | --- |
| Create | `insert` / `insertMany` (returns `_id`) |
| Read | `findById` |
| Update | `replace` (JSON must include `_id`) |
| Delete | `deleteById` |

### Complex queries

```kotlin
db.ensureIndex("users", "age")
db.ensureIndex("users", listOf("address.city", "status"))
db.execute("db.users.find({ age: { \$gte: 21 } }).sort({ name: 1 }).limit(10)")
db.execute("""db.users.find({ ${'$'}and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })""")
db.execute("""db.users.find({ ${'$'}or: [ { age: { ${'$'}lt: 30 } }, { "address.city": "New York" } ] })""")
```

Opening an encrypted file without a key throws `NuvexaEncryptionException`.

## In-repo sample

| Sample | Path |
| --- | --- |
| Java | [src/sampleJava/.../JavaSample.java](src/sampleJava/java/nuventra/nuvexadb/sample/JavaSample.java) |
| Kotlin | [src/sampleKotlin/.../KotlinSample.kt](src/sampleKotlin/kotlin/nuventra/nuvexadb/sample/KotlinSample.kt) |

```bash
cd bindings/jvm
gradle test
gradle runJavaSample
gradle runKotlinSample
```

See [docs/bindings.md](../../docs/bindings.md).
