# C++ library

Header-only RAII (`nuvexa.hpp`) over `nuvexa.h`. CMake target `nuvexadb`.

## Integration

Do not publish a C++ package from this clone. Headers from this tree or CI `NuvexaDB-Cpp-<rid>` plus `NuvexaDB-Native-<rid>`.

### Package / CMake reference

```cmake
add_subdirectory(NuvexaDB/bindings/cpp)
target_link_libraries(your_app PRIVATE nuvexadb)
```

```bash
export NUVEXA_NATIVE_DIR="$(pwd)/artifacts/native/osx-arm64"
```

```cpp
#include "nuvexa.hpp"
```

### Create DB, collection, CRUD, queries

```cpp
auto db = nuvexa::database::create("app.nvx", "sample-key");
auto id = db.insert("users", R"({"name":"Ada","age":36,"status":"active","address":{"city":"London"}})");
db.replace("users", std::string(R"({"_id":")") + id + R"(","name":"Ada Lovelace","age":36,"status":"active","address":{"city":"London"}})");
db.delete_by_id("users", scratch_id);
db.ensure_index("users", "\"age\"");
db.execute("db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)");
db.execute(R"(db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 }))");
db.execute(R"(db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] }))");
```

`ensure_index` takes **fields JSON** (`"age"` or `["address.city","status"]`). First insert creates `users`. Failures throw `nuvexa::encryption_error` / `integrity_error` / `error`.

## In-repo sample

[examples/sample.cpp](examples/sample.cpp)

```bash
cmake -S bindings/cpp -B bindings/cpp/build
cmake --build bindings/cpp/build
ctest --test-dir bindings/cpp/build --output-on-failure
./bindings/cpp/build/nuvexa_sample
```

See [docs/bindings.md](../../docs/bindings.md).
