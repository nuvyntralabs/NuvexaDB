# NuvexaDB Android AAR

Same Kotlin API as `bindings/jvm`. Loads `libnuvexa.so` from `jniLibs`. Sample: [sample/MainActivity.kt](sample/MainActivity.kt).

```bash
src/Nuventra.NuvexaDB.Native/publish.sh "" android-arm64
cp artifacts/native/android-arm64/libnuvexa.so \
  bindings/android/src/main/jniLibs/arm64-v8a/
```

Requires the Android / .NET Android workload for `android-arm64` Native AOT. Desktop JVM samples work without that workload.
