package nuventra.nuvexadb.rn

import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod
import nuventra.nuvexadb.NuvexaDatabase
import nuventra.nuvexadb.NuvexaEncryptionException
import nuventra.nuvexadb.NuvexaException
import nuventra.nuvexadb.NuvexaIntegrityException
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicLong

class NuvexaModule(reactContext: ReactApplicationContext) : ReactContextBaseJavaModule(reactContext) {
    private val handles = ConcurrentHashMap<Long, NuvexaDatabase>()
    private val next = AtomicLong(1)

    override fun getName(): String = "NuvexaDB"

    @ReactMethod
    fun create(path: String, key: String?, promise: Promise) =
        run(promise) { register(NuvexaDatabase.create(path, key)) }

    @ReactMethod
    fun open(path: String, key: String?, promise: Promise) =
        run(promise) { register(NuvexaDatabase.open(path, key)) }

    @ReactMethod
    fun close(handle: Double, promise: Promise) = run(promise) {
        db(handle).close()
        handles.remove(handle.toLong())
        null
    }

    @ReactMethod
    fun isEncrypted(path: String, promise: Promise) =
        run(promise) { NuvexaDatabase.isEncrypted(path) }

    @ReactMethod
    fun insert(handle: Double, collection: String, json: String, promise: Promise) =
        run(promise) { db(handle).insert(collection, json) }

    @ReactMethod
    fun replace(handle: Double, collection: String, json: String, promise: Promise) =
        run(promise) { db(handle).replace(collection, json); null }

    @ReactMethod
    fun deleteById(handle: Double, collection: String, id: String, promise: Promise) =
        run(promise) { db(handle).deleteById(collection, id) }

    @ReactMethod
    fun findById(handle: Double, collection: String, id: String, promise: Promise) =
        run(promise) { db(handle).findById(collection, id)?.toString() }

    @ReactMethod
    fun execute(handle: Double, nql: String, promise: Promise) =
        run(promise) {
            val rows = db(handle).execute(nql).map { it.json }
            org.json.JSONArray(rows).toString()
        }

    @ReactMethod
    fun ensureIndex(handle: Double, collection: String, fieldsJson: String, promise: Promise) =
        run(promise) {
            val node = org.json.JSONTokener(fieldsJson).nextValue()
            val fields = if (node is org.json.JSONArray) {
                (0 until node.length()).map { node.getString(it) }
            } else {
                listOf(node.toString())
            }
            db(handle).ensureIndex(collection, fields)
            null
        }

    @ReactMethod
    fun abiVersion(promise: Promise) = run(promise) { NuvexaDatabase.abiVersion() }

    @ReactMethod
    fun insertMany(handle: Double, collection: String, jsonArray: String, promise: Promise) =
        run(promise) { org.json.JSONArray(db(handle).insertMany(collection, jsonArray)).toString() }

    @ReactMethod
    fun listCollections(handle: Double, promise: Promise) =
        run(promise) { org.json.JSONArray(db(handle).listCollections()).toString() }

    @ReactMethod
    fun dropCollection(handle: Double, collection: String, promise: Promise) =
        run(promise) { db(handle).dropCollection(collection); null }

    @ReactMethod
    fun renameCollection(handle: Double, from: String, to: String, promise: Promise) =
        run(promise) { db(handle).renameCollection(from, to); null }

    @ReactMethod
    fun listIndexes(handle: Double, collection: String, promise: Promise) =
        run(promise) { org.json.JSONArray(db(handle).listIndexes(collection)).toString() }

    @ReactMethod
    fun dropIndex(handle: Double, collection: String, name: String, promise: Promise) =
        run(promise) { db(handle).dropIndex(collection, name); null }

    @ReactMethod
    fun count(handle: Double, collection: String, filterJson: String, promise: Promise) =
        run(promise) { db(handle).count(collection, filterJson) }

    @ReactMethod
    fun checkpoint(handle: Double, promise: Promise) =
        run(promise) { db(handle).checkpoint(); null }

    @ReactMethod
    fun backup(handle: Double, destPath: String, promise: Promise) =
        run(promise) { db(handle).backup(destPath); null }

    @ReactMethod
    fun compact(handle: Double, promise: Promise) =
        run(promise) { db(handle).compact(); null }

    @ReactMethod
    fun restore(backupPath: String, destPath: String, overwrite: Boolean, promise: Promise) =
        run(promise) { NuvexaDatabase.restore(backupPath, destPath, overwrite); null }

    @ReactMethod
    fun stats(handle: Double, promise: Promise) =
        run(promise) { db(handle).stats().toString() }

    @ReactMethod
    fun changeEncryptionKey(handle: Double, currentKey: String, nextKey: String, promise: Promise) =
        run(promise) { db(handle).changeEncryptionKey(currentKey, nextKey); null }

    @ReactMethod
    fun beginTransaction(handle: Double, promise: Promise) =
        run(promise) { db(handle).beginTransaction(); null }

    @ReactMethod
    fun commit(handle: Double, promise: Promise) =
        run(promise) { db(handle).commit(); null }

    @ReactMethod
    fun rollback(handle: Double, promise: Promise) =
        run(promise) { db(handle).rollback(); null }

    @ReactMethod
    fun uploadFile(handle: Double, fileName: String, sourcePath: String, chunkSize: Double, promise: Promise) =
        run(promise) { db(handle).uploadFile(fileName, sourcePath, chunkSize.toInt()) }

    @ReactMethod
    fun downloadFile(handle: Double, fileId: String, destPath: String, promise: Promise) =
        run(promise) { db(handle).downloadFile(fileId, destPath) }

    @ReactMethod
    fun fileMetadata(handle: Double, fileId: String, promise: Promise) =
        run(promise) { db(handle).fileMetadata(fileId)?.toString() }

    private fun register(db: NuvexaDatabase): Double {
        val id = next.getAndIncrement()
        handles[id] = db
        return id.toDouble()
    }

    private fun db(handle: Double): NuvexaDatabase =
        handles[handle.toLong()] ?: throw NuvexaException("Unknown database handle.")

    private fun run(promise: Promise, work: () -> Any?) {
        try {
            promise.resolve(work())
        } catch (ex: NuvexaEncryptionException) {
            promise.reject("ENCRYPTION", ex.message, ex)
        } catch (ex: NuvexaIntegrityException) {
            promise.reject("INTEGRITY", ex.message, ex)
        } catch (ex: NuvexaException) {
            promise.reject("ERROR", ex.message, ex)
        } catch (ex: Exception) {
            promise.reject("ERROR", ex.message, ex)
        }
    }
}
