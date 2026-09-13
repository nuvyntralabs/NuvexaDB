#include "nuvexa.hpp"

#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <string>

namespace fs = std::filesystem;

static int failures = 0;

static void expect(bool ok, const char* message) {
    if (!ok) {
        std::cerr << "FAIL " << message << '\n';
        failures++;
    }
}

int main() {
    if (std::getenv("NUVEXA_NATIVE_LIB") == nullptr && std::getenv("NUVEXA_NATIVE_DIR") == nullptr) {
        std::cout << "skip: set NUVEXA_NATIVE_LIB or NUVEXA_NATIVE_DIR\n";
        return 0;
    }

    expect(nuvexa::abi_version() == 2, "abi version is 2");

    const auto dir = fs::temp_directory_path() / ("nuvexa-cpp-" + std::to_string(std::rand()));
    fs::create_directories(dir);
    const auto path = (dir / "golden.nvx").string();

    {
        auto db = nuvexa::database::create(path, "correct-horse");
        db.insert("users", R"({"name":"Ada","age":36,"city":"London"})");
        db.insert("users", R"({"name":"Cara","age":21,"city":"Bengaluru"})");
        db.insert("users", R"({"name":"Ben","age":12,"city":"Bengaluru"})");
        db.ensure_index("users", R"(["age"])");
        db.ensure_index("users", R"(["city"])");
        expect(db.count("users") == 3, "seed count");
        const auto adults = db.execute(R"(db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(20))");
        expect(adults.find("Ada") != std::string::npos && adults.find("Cara") != std::string::npos, "adults NQL");
        db.begin_transaction();
        db.insert("users", R"({"name":"Zoe","age":40})");
        db.rollback();
        expect(db.count("users") == 3, "rollback restores count");
        expect(db.list_collections().find("users") != std::string::npos, "list collections");
    }

    expect(nuvexa::is_encrypted(path), "file is encrypted");
    try {
        auto opened = nuvexa::database::open(path);
        expect(false, "open without key should throw");
    } catch (const nuvexa::encryption_error&) {
        expect(true, "fail-closed open");
    }

    fs::remove_all(dir);
    if (failures != 0) {
        std::cerr << failures << " assertion(s) failed\n";
        return 1;
    }
    std::cout << "ok\n";
    return 0;
}
