# Java and Kotlin (desktop)

JNA wrapper over the C ABI. You need **two** zips: the JVM jar and the native library for your OS.

## 1. Download

From [NuvexaDB v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1), pick **one** pair that matches the machine that will run the JVM:

| Your machine | JVM zip | Native zip | Library file inside the native zip |
| --- | --- | --- | --- |
| macOS Apple Silicon | `NuvexaDB-Java-osx-arm64.zip` | `NuvexaDB-Native-osx-arm64.zip` | `libnuvexa.dylib` |
| Linux x64 | `NuvexaDB-Java-linux-x64.zip` | `NuvexaDB-Native-linux-x64.zip` | `libnuvexa.so` |
| Windows x64 | `NuvexaDB-Java-win-x64.zip` | `NuvexaDB-Native-win-x64.zip` | `nuvexa.dll` |

The JVM zip contains `jvm-<version>.jar` (and usually `jvm-<version>-sources.jar`). The implementation is the same on every RID; the native zip is what must match the CPU.

For Android, use [android.md](android.md) (`NuvexaDB-Android.zip`) instead of these desktop jars.

## 2. Empty project

```bash
mkdir acme-store && cd acme-store
gradle init --type java-application --dsl kotlin --package com.acme.store
```

Or a Kotlin JVM app with the same Gradle layout.

## 3. Add the downloaded package

Copy the jar next to the project (example `libs/jvm-1.0.1.jar`). In `build.gradle.kts`:

```kotlin
dependencies {
    implementation(files("libs/jvm-1.0.1.jar"))
    implementation("net.java.dev.jna:jna:5.17.0")
    implementation("org.json:json:20250107")
}
```

Point the process at the native file:

```bash
export NUVEXA_NATIVE_LIB="/absolute/path/to/libnuvexa.dylib"   # or .so / nuvexa.dll
```

## 4. Create, open, close

Kotlin:

```kotlin
import nuventra.nuvexadb.NuvexaDatabase

val path = "app.nvx"
NuvexaDatabase.create(path, key).use { db ->
    // work
}

if (NuvexaDatabase.isEncrypted(path) && key == null) {
    error("Encrypted .nvx — pass the key")
}

NuvexaDatabase.open(path, key).use { db ->
    // work; close() runs at the end of use
}
```

Java: `NuvexaDatabase.create(path, key)` and try-with-resources (`AutoCloseable`). Pass `null` for a plaintext file.

## 5. Delete the database

Close first, then delete the file (and `app.nvx-wal` if present):

```kotlin
db.close()
java.nio.file.Files.deleteIfExists(java.nio.file.Path.of(path))
java.nio.file.Files.deleteIfExists(java.nio.file.Path.of(path + "-wal"))
```

## 6. Collections

The first `insert` into a name creates that collection.

```kotlin
db.insert("users", """{"name":"Ada","age":36}""")
println(db.listCollections())
db.renameCollection("users", "people")
db.dropCollection("people")
```

## 7. Documents

```kotlin
val id = db.insert("users", """{"name":"Ada","age":36}""")
db.insertMany("users", """[{"name":"Grace","age":85},{"name":"Cara","age":21}]""")
val ada = db.findById("users", id)
db.replace("users", """{"_id":"$id","name":"Ada Lovelace","age":36}""")
val deleted = db.deleteById("users", id)
val rows = db.execute("""db.users.find({ age: { ${'$'}gte: 21 } }).sort({ name: 1 }).limit(20)""")
```

On 1.0.2+: `db.execute("""db.users.update({ name: "Cara" }, { ${'$'}set: { age: 22 } })""")` and `db.execute("""db.users.delete({ age: { ${'$'}lt: 18 } })""")`.

## 8. Password for an encrypted file

Pass the same `key` string to `create` and `open`. Prefer a long random secret stored in the OS keychain or a secrets manager. Do not hard-code `1234` in the repo.

```kotlin
db.changeEncryptionKey(currentKey, nextKey)
```

See [Encryption notes](README.md#encrypted-databases--choose-a-password).
