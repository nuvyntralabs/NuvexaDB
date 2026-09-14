# C++

Header-only C++ wrapper (`nuvexa.hpp`) plus `nuvexa.h`. You link the published shared library.

## 1. Download

From [NuvexaDB v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1):

| Your machine | C++ zip | Native zip | Library file |
| --- | --- | --- | --- |
| macOS Apple Silicon | `NuvexaDB-Cpp-osx-arm64.zip` | `NuvexaDB-Native-osx-arm64.zip` | `libnuvexa.dylib` |
| Linux x64 | `NuvexaDB-Cpp-linux-x64.zip` | `NuvexaDB-Native-linux-x64.zip` | `libnuvexa.so` |

Inside the C++ zip: `nuvexa.h`, `nuvexa.hpp`, `CMakeLists.txt`, `README.md`.

Windows x64: use `NuvexaDB-Native-win-x64.zip` (`nuvexa.dll`) with the same headers from either C++ zip. Link `nuvexa.lib` / the DLL as you would any C library.

## 2. Empty project

```bash
mkdir acme-store && cd acme-store
```

`CMakeLists.txt`:

```cmake
cmake_minimum_required(VERSION 3.16)
project(acme_store LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 17)
add_executable(acme_store main.cpp)
```

## 3. Add the downloaded package

Copy headers from the C++ zip into `include/`. Point CMake at the native folder:

```cmake
set(NUVEXA_NATIVE_DIR "/absolute/path/to/native-unzip")
target_include_directories(acme_store PRIVATE include)
target_link_directories(acme_store PRIVATE "${NUVEXA_NATIVE_DIR}")
target_link_libraries(acme_store PRIVATE nuvexa)
set_target_properties(acme_store PROPERTIES
  BUILD_RPATH "${NUVEXA_NATIVE_DIR}"
  INSTALL_RPATH "${NUVEXA_NATIVE_DIR}")
```

```bash
export NUVEXA_NATIVE_DIR="/absolute/path/to/native-unzip"
cmake -S . -B build && cmake --build build
```

You can also `add_subdirectory` the unpacked C++ zip if you keep its `CMakeLists.txt`.

## 4. Create, open, close

```cpp
#include "nuvexa.hpp"

// create writes format 2. Format 1 files still open.
auto db = nuvexa::database::create("app.nvx", key); // key "" → plaintext
db.close();

if (nuvexa::database::is_encrypted("app.nvx") && key.empty()) {
    throw std::runtime_error("Encrypted .nvx — pass the key");
}
auto opened = nuvexa::database::open("app.nvx", key);
opened.close();
```

The destructor closes the handle.

## 5. Delete the database

```cpp
db.close();
std::remove("app.nvx");
std::remove("app.nvx-wal");
```

## 6. Collections

The first `insert` creates the collection.

```cpp
db.insert("users", R"({"name":"Ada","age":36})");
auto names = db.list_collections(); // JSON array string
```

`nuvexa.hpp` on 1.0.1 does not wrap drop/rename. Call `nuvexa_drop_collection` / `nuvexa_rename_collection` from `nuvexa.h`, or delete the `.nvx` file.

## 7. Documents

```cpp
auto id = db.insert("users", R"({"name":"Ada","age":36})");
db.insert_many("users", R"([{"name":"Grace","age":85}])");
auto ada = db.find_by_id("users", id);
db.replace("users", std::string(R"({"_id":")") + id + R"(","name":"Ada Lovelace","age":36})");
bool gone = db.delete_by_id("users", id);
auto rows = db.execute(R"(db.users.find({ age: { $gte: 21 } }).limit(20))");
```

## 8. Password for an encrypted file

Pass the key into `create` / `open`. Do not compile `1234` into the binary. Rekey through `nuvexa_change_encryption_key` in `nuvexa.h` (not wrapped on `nuvexa.hpp` in 1.0.1).

See [Encryption notes](README.md#encrypted-databases--choose-a-password).
