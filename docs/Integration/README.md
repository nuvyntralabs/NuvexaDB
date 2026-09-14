# Platform integration

Step-by-step guides for each host that can open a NuvexaDB `.nvx` file. Start here, download the zip that matches your OS and CPU, then open the guide for that language.

**Public downloads:** [github.com/nuvyntralabs/NuvexaDB/releases](https://github.com/nuvyntralabs/NuvexaDB/releases)

The current published tag is **[v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1)**. Later tags keep the same zip names. Pick the asset whose suffix matches your machine (`osx-arm64`, `win-x64`, `linux-x64`, …). Do not download every zip.

nuget.org and language package registries are not the install path yet. You add the file from the Release zip (or a CI artifact with the same name).

## Guides

| Host | Guide | Release zip to start with |
| --- | --- | --- |
| .NET / .NET MAUI | [dotnet.md](dotnet.md) | `NuvexaDB-NuGet.zip` |
| Java / Kotlin (desktop) | [java-kotlin.md](java-kotlin.md) | `NuvexaDB-Java-<rid>.zip` **and** `NuvexaDB-Native-<rid>.zip` |
| Android | [android.md](android.md) | `NuvexaDB-Android.zip` |
| Swift (macOS / iOS) | [swift.md](swift.md) | `NuvexaDB-Swift-osx-arm64.zip` + native zip (macOS) or `NuvexaDB-Native-iOS.zip` (iOS) |
| Flutter / Dart | [flutter.md](flutter.md) | `NuvexaDB-Flutter-<rid>.zip` **and** the native zip for that host |
| React Native | [react-native.md](react-native.md) | `NuvexaDB-React-Native-<rid>.zip` plus Android / iOS native packs |
| Python | [python.md](python.md) | `NuvexaDB-Python-<rid>.zip` **and** `NuvexaDB-Native-<rid>.zip` |
| Node.js | [nodejs.md](nodejs.md) | `NuvexaDB-Node-<rid>.zip` **and** `NuvexaDB-Native-<rid>.zip` |
| Go | [go.md](go.md) | `NuvexaDB-Go-<rid>.zip` **and** `NuvexaDB-Native-<rid>.zip` |
| C++ | [cpp.md](cpp.md) | `NuvexaDB-Cpp-<rid>.zip` **and** `NuvexaDB-Native-<rid>.zip` |

`<rid>` is the runtime identifier in the file name. Match it to the table below.

## Native library file names

Language SDKs (except managed .NET) load the Native AOT C ABI. The library **file name** inside `NuvexaDB-Native-*.zip` is:

| Your machine | Download this zip | Library file inside the zip |
| --- | --- | --- |
| macOS Apple Silicon | `NuvexaDB-Native-osx-arm64.zip` | `libnuvexa.dylib` |
| macOS Intel | `NuvexaDB-Native-osx-x64.zip` | `libnuvexa.dylib` |
| Windows x64 | `NuvexaDB-Native-win-x64.zip` | `nuvexa.dll` |
| Linux x64 | `NuvexaDB-Native-linux-x64.zip` | `libnuvexa.so` |
| Android arm64 | `NuvexaDB-Native-android-arm64.zip` | `libnuvexa.so` |
| iOS device + simulator | `NuvexaDB-Native-iOS.zip` | `Nuvexa.xcframework` (and `nuvexa.h`) |

On [v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1) those are the native zips that shipped. Tags after 1.0.1 also publish `NuvexaDB-Native-win-arm64.zip` and `NuvexaDB-Native-linux-arm64.zip` (`nuvexa.dll` / `libnuvexa.so`).

Set `NUVEXA_NATIVE_LIB` to the **full path** of that library file (Python, Node, Flutter desktop, JVM fallback). Set `NUVEXA_NATIVE_DIR` to the **folder** that contains it (Swift, Go, C++).

## Each guide covers

1. Which Release zip and inner file to download  
2. An empty project  
3. Adding the downloaded package  
4. Create / open / close a `.nvx`  
5. Deleting the database file  
6. Collection create / list / rename / drop  
7. Document insert / read / replace / delete  
8. Password choice for an encrypted file  

NQL `db.<collection>.update` / `delete` needs engine **1.0.2 or later**. On 1.0.1 use `replace` and `delete_by_id` (or the .NET collection methods).

## Encrypted databases — choose a password

Opening an encrypted `.nvx` **without** the key fails closed. The engine derives a key-encryption key with Argon2; the file key is AES-256-GCM. Treat the string you pass as `EncryptionKey` / `key` as a secret.

**Do**

- Use a long random secret or a passphrase of several words (not `1234`, not the app name).  
- Generate it once (for example 32 random bytes, then Base64) and store it in the OS keychain, Android Keystore, or iOS Keychain — never in source control or a checked-in config file.  
- Prompt the user when the host is a desktop IDE; the library itself does not show a dialog.  
- Rotate with `ChangeEncryptionKey` / `changeEncryptionKey` / `change_encryption_key` when the account password changes. Close other processes first.  
- Keep a backup of the `.nvx` before you rekey.

**Do not**

- Embed a default key in the binary “for now.”  
- Log the key, put it in crash reports, or send it to analytics.  
- Share one key across unrelated products.  
- Open the same path from two processes at once.

A local demo may use a throwaway string. Production must not.

Related: [Language bindings](../bindings.md), [NQL](../query.md), [File format](../format.md).
