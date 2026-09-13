# NuvexaDB bindings

Shared Native AOT C ABI plus thin SDKs. Each language folder has a **library**, **tests**, a **sample**, and **README.md** with an **Integration** section: package reference, create DB, create collection, CRUD, and complex NQL. .NET samples under `samples/` share [samples/Shared/SampleTour.cs](../samples/Shared/SampleTour.cs). See [docs/bindings.md](../docs/bindings.md).

```
jvm/           Kotlin/Java library + tests + src/sampleJava + src/sampleKotlin
android/       AAR + sample/MainActivity.kt
swift/         Swift Package + Tests + Examples
flutter/       Dart package + test/ + examples/sample.dart
react-native/  JS/TS library + test/ + examples/sample.mjs
python/        ctypes library + tests/ + examples/
node/          Node library + test/ + examples/
go/            cgo library + *_test.go + examples/sample
cpp/           CMake INTERFACE library + tests/ + examples/
```

.NET samples stay under `samples/` (`Console`, `Maui`, `Avalonia`, `Wpf`, `WinUI`, `Uno`). Each has its own `.sln`; they are not in `NuvexaDB.sln`.

CI runs each SDK’s unit tests against `NuvexaDB-Native-<rid>` and uploads `NuvexaDB-<Library>-<rid>`. Host registries stay unpublished.
