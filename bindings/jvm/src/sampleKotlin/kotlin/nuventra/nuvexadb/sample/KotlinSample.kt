package nuventra.nuvexadb.sample

import nuventra.nuvexadb.NuvexaDatabase
import nuventra.nuvexadb.NuvexaEncryptionException
import java.nio.file.Files
import java.nio.file.Path

fun main(args: Array<String>) {
    val path = Path.of(args.getOrNull(0) ?: "sample.nvx")
    Files.deleteIfExists(path)
    val key = "sample-key"

    NuvexaDatabase.create(path.toString(), key).use { db ->
        db.insert("users", """{"name":"Ada","age":36}""")
        db.ensureIndex("users", "age")
        db.execute("db.users.find({ age: { \$gte: 21 } }).limit(20)").forEach(::println)
        println("collections ${db.listCollections()}")
    }

    println("IsEncrypted: ${NuvexaDatabase.isEncrypted(path.toString())}")
    try {
        NuvexaDatabase.open(path.toString())
        error("open without key should fail")
    } catch (ex: NuvexaEncryptionException) {
        println("Lib fail-closed: ${ex.message}")
    }
}
