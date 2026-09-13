package nuventra.nuvexadb

import com.sun.jna.Library
import com.sun.jna.Native
import com.sun.jna.Pointer
import com.sun.jna.ptr.IntByReference
import com.sun.jna.ptr.LongByReference
import com.sun.jna.ptr.PointerByReference

interface NuvexaLibrary : Library {
    fun nuvexa_abi_version(): Int
    fun nuvexa_create(path: String, key: String?, handle: PointerByReference): Int
    fun nuvexa_open(path: String, key: String?, handle: PointerByReference): Int
    fun nuvexa_close(handle: Long): Int
    fun nuvexa_is_encrypted(path: String, encrypted: IntByReference): Int
    fun nuvexa_insert(handle: Long, collection: String, json: String, idOut: PointerByReference): Int
    fun nuvexa_insert_many(handle: Long, collection: String, jsonArray: String, idsOut: PointerByReference): Int
    fun nuvexa_replace(handle: Long, collection: String, json: String): Int
    fun nuvexa_delete_by_id(handle: Long, collection: String, id: String, deleted: IntByReference): Int
    fun nuvexa_find_by_id(handle: Long, collection: String, id: String, jsonOut: PointerByReference): Int
    fun nuvexa_execute(handle: Long, nql: String, jsonOut: PointerByReference): Int
    fun nuvexa_ensure_index(handle: Long, collection: String, fieldsJson: String): Int
    fun nuvexa_list_collections(handle: Long, jsonOut: PointerByReference): Int
    fun nuvexa_drop_collection(handle: Long, collection: String): Int
    fun nuvexa_rename_collection(handle: Long, from: String, to: String): Int
    fun nuvexa_list_indexes(handle: Long, collection: String, jsonOut: PointerByReference): Int
    fun nuvexa_drop_index(handle: Long, collection: String, name: String): Int
    fun nuvexa_count(handle: Long, collection: String, filterJson: String?, count: LongByReference): Int
    fun nuvexa_checkpoint(handle: Long): Int
    fun nuvexa_backup(handle: Long, destPath: String): Int
    fun nuvexa_compact(handle: Long): Int
    fun nuvexa_restore(backupPath: String, destPath: String, overwrite: Int): Int
    fun nuvexa_stats(handle: Long, jsonOut: PointerByReference): Int
    fun nuvexa_change_encryption_key(handle: Long, currentKey: String, nextKey: String): Int
    fun nuvexa_begin_transaction(handle: Long): Int
    fun nuvexa_commit(handle: Long): Int
    fun nuvexa_rollback(handle: Long): Int
    fun nuvexa_fs_upload(handle: Long, fileName: String, sourcePath: String, chunkSize: Int, idOut: PointerByReference): Int
    fun nuvexa_fs_download(handle: Long, fileId: String, destPath: String, found: IntByReference): Int
    fun nuvexa_fs_metadata(handle: Long, fileId: String, jsonOut: PointerByReference): Int
    fun nuvexa_last_error(message: PointerByReference): Int
    fun nuvexa_free(pointer: Pointer?)

    companion object {
        const val OK = 0
        const val ERROR = 1
        const val ENCRYPTION = 2
        const val INTEGRITY = 3
        const val NOT_FOUND = 4
        const val ABI_VERSION = 2

        fun load(libraryPath: String? = System.getenv("NUVEXA_NATIVE_LIB")): NuvexaLibrary {
            if (!libraryPath.isNullOrBlank()) {
                val file = java.io.File(libraryPath)
                if (file.isFile) {
                    System.setProperty("jna.library.path", file.parentFile.absolutePath)
                    return Native.load(file.absolutePath, NuvexaLibrary::class.java)
                }
            }
            return Native.load("nuvexa", NuvexaLibrary::class.java)
        }
    }
}
