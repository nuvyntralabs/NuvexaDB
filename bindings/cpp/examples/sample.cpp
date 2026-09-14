#include "nuvexa.hpp"

#include <cstdio>
#include <iostream>
#include <stdexcept>
#include <string>

int main(int argc, char** argv) {
    const char* path = argc > 1 ? argv[1] : "sample.nvx";
    std::remove(path);
    {
        // create writes format 2
        auto db = nuvexa::database::create(path, "sample-key");
        auto ada_id = db.insert(
            "users",
            R"({"name":"Ada","age":36,"status":"active","address":{"city":"London"}})");
        db.insert_many(
            "users",
            R"([
              {"name":"Grace","age":85,"status":"retired","address":{"city":"New York"}},
              {"name":"Cara","age":21,"status":"active","address":{"city":"Bengaluru"}},
              {"name":"Alan","age":42,"status":"active","address":{"city":"London"}}
            ])");
        auto scratch_id = db.insert(
            "users",
            R"({"name":"Scratch","age":19,"status":"active","address":{"city":"Paris"}})");
        db.ensure_index("users", "\"age\"");
        db.ensure_index("users", R"(["address.city","status"])");
        std::cout << "Created collection users. Collections " << db.list_collections() << '\n';
        std::cout << "Read Ada: " << db.find_by_id("users", ada_id) << '\n';
        db.replace(
            "users",
            std::string(R"({"_id":")") + ada_id
                + R"(","name":"Ada Lovelace","age":36,"status":"active","address":{"city":"London"}})");
        std::cout << "Updated Ada: " << db.find_by_id("users", ada_id) << '\n';
        std::cout << "Deleted scratch: " << db.delete_by_id("users", scratch_id) << '\n';
        std::cout << "-- NQL age >= 21, sort name, limit 10 --\n"
                  << db.execute("db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)") << '\n';
        std::cout << "-- NQL $and London + active --\n"
                  << db.execute(
                         R"(db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 }))")
                  << '\n';
        std::cout << "-- NQL $or age < 30 or New York --\n"
                  << db.execute(
                         R"(db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] }))")
                  << '\n';
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
