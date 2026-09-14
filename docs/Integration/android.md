# Android (Kotlin / Java)

Same Kotlin API as the desktop JVM SDK, packaged as an AAR with `libnuvexa.so` for **arm64-v8a** (`minSdk` 26).

## 1. Download

From [NuvexaDB v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1):

| Release asset | What you use |
| --- | --- |
| **`NuvexaDB-Android.zip`** | The `.aar` inside (often `android-release.aar`). It already embeds `jni/arm64-v8a/libnuvexa.so`. |

You do **not** need `NuvexaDB-Java-*.zip` or a separate `NUVEXA_NATIVE_LIB` on the device. JNA loads `nuvexa` from the APK.

Optional: `NuvexaDB-Native-android-arm64.zip` is the raw `libnuvexa.so` if you assemble the AAR yourself.

x86 emulator images are not a published ABI. Use an arm64 emulator or a physical device.

## 2. Empty project

In Android Studio: **File → New → New Project → Empty Activity** (Kotlin, API 26+).

Or:

```bash
# Android Studio wizard, or
mkdir AcmeStore && cd AcmeStore
# create an application module named app
```

## 3. Add the downloaded package

Copy the AAR to `app/libs/android-release.aar`. In `app/build.gradle.kts`:

```kotlin
dependencies {
    implementation(files("libs/android-release.aar"))
    implementation("net.java.dev.jna:jna:5.17.0@aar")
    implementation("org.json:json:20250107")
}
```

Enable `flatDir` only if your AGP version requires it for `files(...)`.

## 4. Create, open, close

```kotlin
import nuventra.nuvexadb.NuvexaDatabase

val path = File(filesDir, "app.nvx").absolutePath
// create writes format 2. Format 1 files still open.
NuvexaDatabase.create(path, key).use { db ->
    // work
}
val opened = NuvexaDatabase.open(path, key)
try {
    // work
} finally {
    opened.close()
}
```

Use `filesDir` or `noBackupFilesDir`, not external storage, so the `.nvx` stays in the app sandbox.

## 5. Delete the database

```kotlin
db.close()
File(path).delete()
File("$path-wal").delete()
```

## 6. Collections

```kotlin
db.insert("users", """{"name":"Ada","age":36}""")
db.listCollections()
db.renameCollection("users", "people")
db.dropCollection("people")
```

## 7. Documents

```kotlin
val id = db.insert("users", """{"name":"Ada","age":36}""")
db.insertMany("users", """[{"name":"Grace","age":85}]""")
val doc = db.findById("users", id)
db.replace("users", """{"_id":"$id","name":"Ada Lovelace","age":36}""")
db.deleteById("users", id)
db.execute("""db.users.find({ age: { ${'$'}gte: 21 } }).limit(20)""")
```

## 8. Password for an encrypted file

Do not put the key in `BuildConfig` or resources. Store it in **Android Keystore** (or EncryptedSharedPreferences wrapping a Keystore key) and pass that string into `create` / `open`. Prompt on first launch if the product is user-held. `1234` is only for a local sample.

```kotlin
db.changeEncryptionKey(currentKey, nextKey)
```

See [Encryption notes](README.md#encrypted-databases--choose-a-password).
