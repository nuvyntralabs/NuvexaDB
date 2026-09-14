package nuventra.nuvexadb.sample

import nuventra.nuvexadb.NuvexaDatabase
import nuventra.nuvexadb.NuvexaEncryptionException
import java.nio.file.Files
import java.nio.file.Path

fun main(args: Array<String>) {
    val path = Path.of(args.getOrNull(0) ?: "sample.nvx")
    Files.deleteIfExists(path)
    val key = "sample-key"

    // create writes format 2
    NuvexaDatabase.create(path.toString(), key).use { db ->
        val adaId = db.insert(
            "users",
            """{"name":"Ada","age":36,"status":"active","address":{"city":"London"}}"""
        )
        db.insertMany(
            "users",
            """[
              {"name":"Grace","age":85,"status":"retired","address":{"city":"New York"}},
              {"name":"Cara","age":21,"status":"active","address":{"city":"Bengaluru"}},
              {"name":"Alan","age":42,"status":"active","address":{"city":"London"}}
            ]"""
        )
        val scratchId = db.insert(
            "users",
            """{"name":"Scratch","age":19,"status":"active","address":{"city":"Paris"}}"""
        )
        db.ensureIndex("users", "age")
        db.ensureIndex("users", listOf("address.city", "status"))
        println("Created collection users. Collections: ${db.listCollections()}")

        println("Read Ada: ${db.findById("users", adaId)}")
        db.replace(
            "users",
            """{"_id":"$adaId","name":"Ada Lovelace","age":36,"status":"active","address":{"city":"London"}}"""
        )
        println("Updated Ada: ${db.findById("users", adaId)}")
        println("Deleted scratch: ${db.deleteById("users", scratchId)}")

        println("-- NQL age >= 21, sort name, limit 10 --")
        db.execute("db.users.find({ age: { \$gte: 21 } }).sort({ name: 1 }).limit(10)").forEach(::println)
        println("-- NQL \$and London + active --")
        db.execute("""db.users.find({ ${'$'}and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })""")
            .forEach(::println)
        println("-- NQL \$or age < 30 or New York --")
        db.execute("""db.users.find({ ${'$'}or: [ { age: { ${'$'}lt: 30 } }, { "address.city": "New York" } ] })""")
            .forEach(::println)
    }

    println("IsEncrypted: ${NuvexaDatabase.isEncrypted(path.toString())}")
    try {
        NuvexaDatabase.open(path.toString())
        error("open without key should fail")
    } catch (ex: NuvexaEncryptionException) {
        println("Lib fail-closed: ${ex.message}")
    }
}
