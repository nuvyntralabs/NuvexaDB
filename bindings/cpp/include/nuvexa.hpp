#pragma once

#include "nuvexa.h"

#include <cstdint>
#include <stdexcept>
#include <string>
#include <utility>

namespace nuvexa {

inline constexpr int abi_version_number = NUVEXA_ABI_VERSION;

class error : public std::runtime_error {
public:
    explicit error(int status, std::string message)
        : std::runtime_error(std::move(message)), status_(status) {}

    int status() const noexcept { return status_; }

private:
    int status_;
};

class encryption_error : public error {
public:
    explicit encryption_error(std::string message) : error(NUVEXA_ENCRYPTION, std::move(message)) {}
};

class integrity_error : public error {
public:
    explicit integrity_error(std::string message) : error(NUVEXA_INTEGRITY, std::move(message)) {}
};

inline std::string last_error() {
    char* message = nullptr;
    nuvexa_last_error(&message);
    if (message == nullptr) {
        return "NuvexaDB native call failed.";
    }
    std::string text(message);
    nuvexa_free(message);
    return text.empty() ? "NuvexaDB native call failed." : text;
}

inline void check(nuvexa_status status) {
    if (status == NUVEXA_OK) {
        return;
    }
    auto message = last_error();
    if (status == NUVEXA_ENCRYPTION) {
        throw encryption_error(std::move(message));
    }
    if (status == NUVEXA_INTEGRITY) {
        throw integrity_error(std::move(message));
    }
    throw error(status, std::move(message));
}

inline std::string take(char* pointer) {
    if (pointer == nullptr) {
        return {};
    }
    std::string text(pointer);
    nuvexa_free(pointer);
    return text;
}

inline int abi_version() { return nuvexa_abi_version(); }

inline bool is_encrypted(const std::string& path) {
    int32_t flag = 0;
    check(nuvexa_is_encrypted(path.c_str(), &flag));
    return flag != 0;
}

inline void restore(const std::string& backup, const std::string& dest, bool overwrite = false) {
    check(nuvexa_restore(backup.c_str(), dest.c_str(), overwrite ? 1 : 0));
}

class database {
public:
    static database create(const std::string& path, const std::string& key = {}) {
        return open_or_create(path, key, true);
    }

    static database open(const std::string& path, const std::string& key = {}) {
        return open_or_create(path, key, false);
    }

    database(const database&) = delete;
    database& operator=(const database&) = delete;

    database(database&& other) noexcept : handle_(other.handle_) { other.handle_ = 0; }

    database& operator=(database&& other) noexcept {
        if (this != &other) {
            close_silent();
            handle_ = other.handle_;
            other.handle_ = 0;
        }
        return *this;
    }

    ~database() { close_silent(); }

    void close() {
        if (handle_ == 0) {
            return;
        }
        auto handle = handle_;
        handle_ = 0;
        check(nuvexa_close(handle));
    }

    std::string insert(const std::string& collection, const std::string& json) {
        char* id = nullptr;
        check(nuvexa_insert(handle_, collection.c_str(), json.c_str(), &id));
        return take(id);
    }

    std::string insert_many(const std::string& collection, const std::string& json_array) {
        char* ids = nullptr;
        check(nuvexa_insert_many(handle_, collection.c_str(), json_array.c_str(), &ids));
        return take(ids);
    }

    void replace(const std::string& collection, const std::string& json) {
        check(nuvexa_replace(handle_, collection.c_str(), json.c_str()));
    }

    bool delete_by_id(const std::string& collection, const std::string& id) {
        int32_t deleted = 0;
        check(nuvexa_delete_by_id(handle_, collection.c_str(), id.c_str(), &deleted));
        return deleted != 0;
    }

    std::string find_by_id(const std::string& collection, const std::string& id) {
        char* json = nullptr;
        auto status = nuvexa_find_by_id(handle_, collection.c_str(), id.c_str(), &json);
        if (status == NUVEXA_NOT_FOUND) {
            if (json) {
                nuvexa_free(json);
            }
            return {};
        }
        check(status);
        return take(json);
    }

    std::string execute(const std::string& nql) {
        char* json = nullptr;
        check(nuvexa_execute(handle_, nql.c_str(), &json));
        return take(json);
    }

    void ensure_index(const std::string& collection, const std::string& fields_json) {
        check(nuvexa_ensure_index(handle_, collection.c_str(), fields_json.c_str()));
    }

    std::string list_collections() {
        char* json = nullptr;
        check(nuvexa_list_collections(handle_, &json));
        return take(json);
    }

    std::int64_t count(const std::string& collection, const std::string& filter_json = "{}") {
        std::int64_t n = 0;
        check(nuvexa_count(handle_, collection.c_str(), filter_json.c_str(), &n));
        return n;
    }

    void begin_transaction() { check(nuvexa_begin_transaction(handle_)); }
    void commit() { check(nuvexa_commit(handle_)); }
    void rollback() { check(nuvexa_rollback(handle_)); }

    std::string stats() {
        char* json = nullptr;
        check(nuvexa_stats(handle_, &json));
        return take(json);
    }

    std::string upload_file(const std::string& file_name, const std::string& source_path, int chunk_size = 0) {
        char* id = nullptr;
        check(nuvexa_fs_upload(handle_, file_name.c_str(), source_path.c_str(), chunk_size, &id));
        return take(id);
    }

    bool download_file(const std::string& file_id, const std::string& dest_path) {
        int32_t found = 0;
        check(nuvexa_fs_download(handle_, file_id.c_str(), dest_path.c_str(), &found));
        return found != 0;
    }

private:
    explicit database(nuvexa_handle handle) : handle_(handle) {}

    static database open_or_create(const std::string& path, const std::string& key, bool create) {
        nuvexa_handle handle = 0;
        auto status = create
            ? nuvexa_create(path.c_str(), key.empty() ? nullptr : key.c_str(), &handle)
            : nuvexa_open(path.c_str(), key.empty() ? nullptr : key.c_str(), &handle);
        check(status);
        return database(handle);
    }

    void close_silent() noexcept {
        if (handle_ != 0) {
            nuvexa_close(handle_);
            handle_ = 0;
        }
    }

    nuvexa_handle handle_ = 0;
};

} // namespace nuvexa
