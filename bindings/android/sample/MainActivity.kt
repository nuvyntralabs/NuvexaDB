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
            db.insert("users", """{"name":"Ada","age":36}""")
            db.ensureIndex("users", "age")
            db.execute("db.users.find({ age: { \$gte: 21 } }).limit(20)").forEach { log.appendLine(it) }
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
