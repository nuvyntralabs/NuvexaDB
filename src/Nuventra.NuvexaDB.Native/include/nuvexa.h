#ifndef NUVEXA_H
#define NUVEXA_H

#include <stdint.h>

#if defined(_WIN32) && defined(NUVEXA_DLLIMPORT)
#define NUVEXA_API __declspec(dllimport)
#else
#define NUVEXA_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum {
    NUVEXA_OK = 0,
    NUVEXA_ERROR = 1,
    NUVEXA_ENCRYPTION = 2,
    NUVEXA_INTEGRITY = 3,
    NUVEXA_NOT_FOUND = 4
};

typedef int32_t nuvexa_status;
typedef intptr_t nuvexa_handle;

/* Current C ABI. Bump when symbols or JSON shapes change. */
#define NUVEXA_ABI_VERSION 2

NUVEXA_API int32_t nuvexa_abi_version(void);

NUVEXA_API nuvexa_status nuvexa_create(const char* path, const char* key, nuvexa_handle* out_handle);
NUVEXA_API nuvexa_status nuvexa_open(const char* path, const char* key, nuvexa_handle* out_handle);
NUVEXA_API nuvexa_status nuvexa_close(nuvexa_handle handle);
NUVEXA_API nuvexa_status nuvexa_is_encrypted(const char* path, int32_t* encrypted);
NUVEXA_API nuvexa_status nuvexa_insert(nuvexa_handle handle, const char* collection, const char* json, char** id_out);
NUVEXA_API nuvexa_status nuvexa_insert_many(nuvexa_handle handle, const char* collection, const char* json_array, char** ids_out);
NUVEXA_API nuvexa_status nuvexa_replace(nuvexa_handle handle, const char* collection, const char* json);
NUVEXA_API nuvexa_status nuvexa_delete_by_id(nuvexa_handle handle, const char* collection, const char* id, int32_t* deleted);
NUVEXA_API nuvexa_status nuvexa_find_by_id(nuvexa_handle handle, const char* collection, const char* id, char** json_out);
NUVEXA_API nuvexa_status nuvexa_execute(nuvexa_handle handle, const char* nql, char** json_out);
NUVEXA_API nuvexa_status nuvexa_ensure_index(nuvexa_handle handle, const char* collection, const char* fields_json);
NUVEXA_API nuvexa_status nuvexa_list_collections(nuvexa_handle handle, char** json_out);
NUVEXA_API nuvexa_status nuvexa_drop_collection(nuvexa_handle handle, const char* collection);
NUVEXA_API nuvexa_status nuvexa_rename_collection(nuvexa_handle handle, const char* from, const char* to);
NUVEXA_API nuvexa_status nuvexa_list_indexes(nuvexa_handle handle, const char* collection, char** json_out);
NUVEXA_API nuvexa_status nuvexa_drop_index(nuvexa_handle handle, const char* collection, const char* name);
NUVEXA_API nuvexa_status nuvexa_count(nuvexa_handle handle, const char* collection, const char* filter_json, int64_t* count);
NUVEXA_API nuvexa_status nuvexa_checkpoint(nuvexa_handle handle);
NUVEXA_API nuvexa_status nuvexa_backup(nuvexa_handle handle, const char* dest_path);
NUVEXA_API nuvexa_status nuvexa_compact(nuvexa_handle handle);
NUVEXA_API nuvexa_status nuvexa_restore(const char* backup_path, const char* dest_path, int32_t overwrite);
NUVEXA_API nuvexa_status nuvexa_stats(nuvexa_handle handle, char** json_out);
NUVEXA_API nuvexa_status nuvexa_change_encryption_key(nuvexa_handle handle, const char* current_key, const char* next_key);
NUVEXA_API nuvexa_status nuvexa_begin_transaction(nuvexa_handle handle);
NUVEXA_API nuvexa_status nuvexa_commit(nuvexa_handle handle);
NUVEXA_API nuvexa_status nuvexa_rollback(nuvexa_handle handle);
NUVEXA_API nuvexa_status nuvexa_fs_upload(nuvexa_handle handle, const char* file_name, const char* source_path, int32_t chunk_size, char** id_out);
NUVEXA_API nuvexa_status nuvexa_fs_download(nuvexa_handle handle, const char* file_id, const char* dest_path, int32_t* found);
NUVEXA_API nuvexa_status nuvexa_fs_metadata(nuvexa_handle handle, const char* file_id, char** json_out);
NUVEXA_API nuvexa_status nuvexa_last_error(char** message);
NUVEXA_API void nuvexa_free(char* pointer);

#ifdef __cplusplus
}
#endif

#endif
