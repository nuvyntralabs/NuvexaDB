# NuvexaDB Android AAR

Same Kotlin API as [`bindings/jvm`](../jvm). The AAR ships `libnuvexa.so` in `jni/arm64-v8a/`. `minSdk` 26, **arm64-v8a only**.

## Integration

Download `NuvexaDB-Android` from CI, or assemble this project after staging `NuvexaDB-Native-android-arm64`.

### Package reference

```kotlin
implementation(files("nuvexadb-android-release.aar"))
implementation("net.java.dev.jna:jna:5.17.0@aar")
implementation("org.json:json:20250107")
```

```bash
src/Nuventra.NuvexaDB.Native/publish.sh "" android-arm64
cp artifacts/native/android-arm64/libnuvexa.so \
  bindings/android/src/main/jniLibs/arm64-v8a/
```

### Create DB, collection, CRUD, queries

Use the app files directory. First insert creates `users`.

```kotlin
val path = filesDir.resolve("app.nvx").absolutePath
// create writes format 2. Format 1 files still open.
NuvexaDatabase.create(path, "sample-key").use { db ->
    val id = db.insert("users", """{"name":"Ada","age":36,"status":"active","address":{"city":"London"}}""")
    db.replace("users", """{"_id":"$id","name":"Ada Lovelace","age":36,"status":"active","address":{"city":"London"}}""")
    db.execute("db.users.find({ age: { \$gte: 21 } }).sort({ name: 1 }).limit(10)")
    db.execute("""db.users.find({ ${'$'}and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })""")
    db.execute("""db.users.find({ ${'$'}or: [ { age: { ${'$'}lt: 30 } }, { "address.city": "New York" } ] })""")
}
```

Full tour: [sample/MainActivity.kt](sample/MainActivity.kt). Fail-closed open throws `NuvexaEncryptionException`.

See [docs/bindings.md](../../docs/bindings.md).
