package nuventra.nuvexadb

import com.sun.jna.Pointer
import com.sun.jna.ptr.IntByReference
import com.sun.jna.ptr.LongByReference
import com.sun.jna.ptr.PointerByReference
import org.json.JSONArray
import org.json.JSONObject

class NuvexaDatabase private constructor(
    private val lib: NuvexaLibrary,
    private var handle: Long
) : AutoCloseable {

    fun insert(collection: String, json: String): String {
        val out = PointerByReference()
        check(lib.nuvexa_insert(handle, collection, json, out), out)
        return readAndFree(out)
    }

    fun insert(collection: String, document: NuvexaDocument): String =
        insert(collection, document.json.toString())

    fun replace(collection: String, json: String) {
        check(lib.nuvexa_replace(handle, collection, json))
    }

    fun deleteById(collection: String, id: String): Boolean {
        val deleted = IntByReference()
        check(lib.nuvexa_delete_by_id(handle, collection, id, deleted))
        return deleted.value != 0
    }

    fun findById(collection: String, id: String): NuvexaDocument? {
        val out = PointerByReference()
        val status = lib.nuvexa_find_by_id(handle, collection, id, out)
        if (status == NuvexaLibrary.NOT_FOUND) {
            free(out)
            return null
        }
        check(status, out)
        return NuvexaDocument.parse(readAndFree(out))
    }

    fun execute(nql: String): List<NuvexaDocument> {
        val out = PointerByReference()
        check(lib.nuvexa_execute(handle, nql, out), out)
        val array = JSONArray(readAndFree(out))
        return (0 until array.length()).map { NuvexaDocument(array.getJSONObject(it)) }
    }

    fun ensureIndex(collection: String, fields: List<String>) {
        val payload = if (fields.size == 1) JSONObject.quote(fields[0]) else JSONArray(fields).toString()
        check(lib.nuvexa_ensure_index(handle, collection, payload))
    }

    fun ensureIndex(collection: String, field: String) = ensureIndex(collection, listOf(field))

    fun insertMany(collection: String, jsonArray: String): List<String> {
        val out = PointerByReference()
        check(lib.nuvexa_insert_many(handle, collection, jsonArray, out), out)
        val array = JSONArray(readAndFree(out))
        return (0 until array.length()).map { array.getString(it) }
    }

    fun listCollections(): List<String> {
        val out = PointerByReference()
        check(lib.nuvexa_list_collections(handle, out), out)
        val array = JSONArray(readAndFree(out))
        return (0 until array.length()).map { array.getString(it) }
    }

    fun dropCollection(collection: String) {
        check(lib.nuvexa_drop_collection(handle, collection))
    }

    fun renameCollection(from: String, to: String) {
        check(lib.nuvexa_rename_collection(handle, from, to))
    }

    fun listIndexes(collection: String): List<JSONObject> {
        val out = PointerByReference()
        check(lib.nuvexa_list_indexes(handle, collection, out), out)
        val array = JSONArray(readAndFree(out))
        return (0 until array.length()).map { array.getJSONObject(it) }
    }

    fun dropIndex(collection: String, name: String) {
        check(lib.nuvexa_drop_index(handle, collection, name))
    }

    @JvmOverloads
    fun count(collection: String, filterJson: String = "{}"): Long {
        val n = LongByReference()
        check(lib.nuvexa_count(handle, collection, filterJson, n))
        return n.value
    }

    fun checkpoint() {
        check(lib.nuvexa_checkpoint(handle))
    }

    fun backup(destPath: String) {
        check(lib.nuvexa_backup(handle, destPath))
    }

    fun compact() {
        check(lib.nuvexa_compact(handle))
    }

    fun stats(): JSONObject {
        val out = PointerByReference()
        check(lib.nuvexa_stats(handle, out), out)
        return JSONObject(readAndFree(out))
    }

    fun changeEncryptionKey(currentKey: String, nextKey: String) {
        check(lib.nuvexa_change_encryption_key(handle, currentKey, nextKey))
    }

    fun beginTransaction() {
        check(lib.nuvexa_begin_transaction(handle))
    }

    fun commit() {
        check(lib.nuvexa_commit(handle))
    }

    fun rollback() {
        check(lib.nuvexa_rollback(handle))
    }

    @JvmOverloads
    fun uploadFile(fileName: String, sourcePath: String, chunkSize: Int = 0): String {
        val out = PointerByReference()
        check(lib.nuvexa_fs_upload(handle, fileName, sourcePath, chunkSize, out), out)
        return readAndFree(out)
    }

    fun downloadFile(fileId: String, destPath: String): Boolean {
        val found = IntByReference()
        check(lib.nuvexa_fs_download(handle, fileId, destPath, found))
        return found.value != 0
    }

    fun fileMetadata(fileId: String): NuvexaDocument? {
        val out = PointerByReference()
        val status = lib.nuvexa_fs_metadata(handle, fileId, out)
        if (status == NuvexaLibrary.NOT_FOUND) {
            free(out)
            return null
        }
        check(status, out)
        return NuvexaDocument.parse(readAndFree(out))
    }

    override fun close() {
        if (handle == 0L) {
            return
        }
        val status = lib.nuvexa_close(handle)
        handle = 0
        check(status)
    }

    private fun check(status: Int, out: PointerByReference? = null) {
        if (status == NuvexaLibrary.OK) {
            return
        }
        val message = lastError()
        free(out)
        throw when (status) {
            NuvexaLibrary.ENCRYPTION -> NuvexaEncryptionException(message)
            NuvexaLibrary.INTEGRITY -> NuvexaIntegrityException(message)
            else -> NuvexaException(message)
        }
    }

    private fun lastError(): String {
        val out = PointerByReference()
        lib.nuvexa_last_error(out)
        return readAndFree(out).ifBlank { "NuvexaDB native call failed." }
    }

    private fun readAndFree(out: PointerByReference): String {
        val pointer = out.value ?: return ""
        return try {
            pointer.getString(0, "UTF-8")
        } finally {
            lib.nuvexa_free(pointer)
        }
    }

    private fun free(out: PointerByReference?) {
        val pointer: Pointer = out?.value ?: return
        lib.nuvexa_free(pointer)
    }

    companion object {
        private val defaultLib: NuvexaLibrary by lazy { NuvexaLibrary.load() }

        @JvmStatic
        @JvmOverloads
        fun create(path: String, key: String? = null, library: NuvexaLibrary = defaultLib): NuvexaDatabase =
            openOrCreate(path, key, create = true, library)

        @JvmStatic
        @JvmOverloads
        fun open(path: String, key: String? = null, library: NuvexaLibrary = defaultLib): NuvexaDatabase =
            openOrCreate(path, key, create = false, library)

        @JvmStatic
        fun abiVersion(library: NuvexaLibrary = defaultLib): Int = library.nuvexa_abi_version()

        @JvmStatic
        @JvmOverloads
        fun restore(backupPath: String, destPath: String, overwrite: Boolean = false, library: NuvexaLibrary = defaultLib) {
            val status = library.nuvexa_restore(backupPath, destPath, if (overwrite) 1 else 0)
            if (status != NuvexaLibrary.OK) {
                val out = PointerByReference()
                library.nuvexa_last_error(out)
                val message = out.value?.getString(0, "UTF-8") ?: "NuvexaDB native call failed."
                if (out.value != null) {
                    library.nuvexa_free(out.value)
                }
                throw NuvexaException(message)
            }
        }

        @JvmStatic
        @JvmOverloads
        fun isEncrypted(path: String, library: NuvexaLibrary = defaultLib): Boolean {
            val flag = IntByReference()
            val status = library.nuvexa_is_encrypted(path, flag)
            if (status != NuvexaLibrary.OK) {
                val out = PointerByReference()
                library.nuvexa_last_error(out)
                val message = out.value?.getString(0, "UTF-8") ?: "NuvexaDB native call failed."
                if (out.value != null) {
                    library.nuvexa_free(out.value)
                }
                throw if (status == NuvexaLibrary.ENCRYPTION) {
                    NuvexaEncryptionException(message)
                } else {
                    NuvexaException(message)
                }
            }
            return flag.value != 0
        }

        private fun openOrCreate(
            path: String,
            key: String?,
            create: Boolean,
            library: NuvexaLibrary
        ): NuvexaDatabase {
            val handle = PointerByReference()
            val status = if (create) library.nuvexa_create(path, key, handle) else library.nuvexa_open(path, key, handle)
            if (status != NuvexaLibrary.OK) {
                val out = PointerByReference()
                library.nuvexa_last_error(out)
                val message = out.value?.getString(0, "UTF-8") ?: "NuvexaDB native call failed."
                if (out.value != null) {
                    library.nuvexa_free(out.value)
                }
                throw when (status) {
                    NuvexaLibrary.ENCRYPTION -> NuvexaEncryptionException(message)
                    NuvexaLibrary.INTEGRITY -> NuvexaIntegrityException(message)
                    else -> NuvexaException(message)
                }
            }
            return NuvexaDatabase(library, Pointer.nativeValue(handle.value))
        }
    }
}
