# C++ library

Header-only RAII overlay over `nuvexa.h`. ABI v2. CMake builds the tests and the in-tree sample.

| Piece | Path |
| --- | --- |
| Library | `include/nuvexa.hpp` + `include/nuvexa.h` (`CMakeLists.txt`) |
| Tests | `tests/interop.cpp` |
| Sample | [examples/sample.cpp](examples/sample.cpp) |
| This file | `README.md` |

```bash
# from the NuvexaDB repo root
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_DIR="$(pwd)/artifacts/native/osx-arm64"
export NUVEXA_NATIVE_LIB="$NUVEXA_NATIVE_DIR/libnuvexa.dylib"
cmake -S bindings/cpp -B bindings/cpp/build
cmake --build bindings/cpp/build
ctest --test-dir bindings/cpp/build --output-on-failure
./bindings/cpp/build/nuvexa_sample
```

```cpp
auto db = nuvexa::database::create("app.nvx", "correct-horse");
db.insert("users", R"({"name":"Ada","age":36})");
auto rows = db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)");
```

Do not publish a C++ package from this clone.
