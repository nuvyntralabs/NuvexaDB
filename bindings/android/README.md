# NuvexaDB Android AAR

Same Kotlin API as `bindings/jvm`. Loads `libnuvexa.so` from `jniLibs`. Sample: [sample/MainActivity.kt](sample/MainActivity.kt).

```bash
src/Nuventra.NuvexaDB.Native/publish.sh "" android-arm64
cp artifacts/native/android-arm64/libnuvexa.so \
  bindings/android/src/main/jniLibs/arm64-v8a/
```

CI publishes `NuvexaDB-Native-android-arm64` and packs it into the AAR (`jni/arm64-v8a/libnuvexa.so`). Desktop Java samples work without the Android workload.
