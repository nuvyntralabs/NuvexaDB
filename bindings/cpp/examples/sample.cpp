#include "nuvexa.hpp"

#include <cstdio>
#include <iostream>
#include <stdexcept>

int main(int argc, char** argv) {
    const char* path = argc > 1 ? argv[1] : "sample.nvx";
    std::remove(path);
    {
        auto db = nuvexa::database::create(path, "sample-key");
        db.insert("users", R"({"name":"Ada","age":36})");
        db.ensure_index("users", "\"age\"");
        std::cout << db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)") << '\n';
        std::cout << "collections " << db.list_collections() << '\n';
    }
    std::cout << "encrypted " << nuvexa::is_encrypted(path) << '\n';
    try {
        auto opened = nuvexa::database::open(path);
        throw std::runtime_error("open without key should fail");
    } catch (const nuvexa::encryption_error& ex) {
        std::cout << "fail-closed " << ex.what() << '\n';
    }
    return 0;
}
