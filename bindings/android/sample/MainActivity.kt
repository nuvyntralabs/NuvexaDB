package nuventra.nuvexadb.sample

import android.os.Bundle
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import nuventra.nuvexadb.NuvexaDatabase
import nuventra.nuvexadb.NuvexaEncryptionException

class MainActivity : AppCompatActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val path = filesDir.resolve("app.nvx").absolutePath
        filesDir.resolve("app.nvx").delete()
        val log = StringBuilder()
        NuvexaDatabase.create(path, "sample-key").use { db ->
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
            log.appendLine("Created collection users. Collections: ${db.listCollections()}")
            log.appendLine("Read Ada: ${db.findById("users", adaId)}")
            db.replace(
                "users",
                """{"_id":"$adaId","name":"Ada Lovelace","age":36,"status":"active","address":{"city":"London"}}"""
            )
            log.appendLine("Updated Ada: ${db.findById("users", adaId)}")
            log.appendLine("Deleted scratch: ${db.deleteById("users", scratchId)}")
            log.appendLine("-- NQL age >= 21 --")
            db.execute("db.users.find({ age: { \$gte: 21 } }).sort({ name: 1 }).limit(10)")
                .forEach { log.appendLine(it) }
            log.appendLine("-- NQL \$and London + active --")
            db.execute("""db.users.find({ ${'$'}and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })""")
                .forEach { log.appendLine(it) }
            log.appendLine("-- NQL \$or --")
            db.execute("""db.users.find({ ${'$'}or: [ { age: { ${'$'}lt: 30 } }, { "address.city": "New York" } ] })""")
                .forEach { log.appendLine(it) }
        }
        log.appendLine("IsEncrypted: ${NuvexaDatabase.isEncrypted(path)}")
        try {
            NuvexaDatabase.open(path)
            log.appendLine("ERROR: open without key should have failed.")
        } catch (ex: NuvexaEncryptionException) {
            log.appendLine("Lib fail-closed: ${ex.message}")
        }
        setContentView(TextView(this).apply { text = log.toString() })
    }
}
