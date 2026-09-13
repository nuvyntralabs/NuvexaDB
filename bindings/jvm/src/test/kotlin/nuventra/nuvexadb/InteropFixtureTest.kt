package nuventra.nuvexadb

import org.json.JSONArray
import org.json.JSONObject
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable
import java.nio.file.Files
import java.nio.file.Path

@EnabledIfEnvironmentVariable(named = "NUVEXA_NATIVE_LIB", matches = ".+")
class InteropFixtureTest {
    @Test
    fun goldenCases() {
        val fixture = JSONObject(Files.readString(findCases()))
        val key = fixture.getString("key")
        val collection = fixture.getString("collection")
        val path = Files.createTempFile("nuvexa-jvm-", ".nvx")
        Files.deleteIfExists(path)
        NuvexaDatabase.create(path.toString(), key).use { db ->
            seed(db, fixture, collection)
            assertCases(db, fixture)
        }
        assertTrue(NuvexaDatabase.isEncrypted(path.toString()))
        assertThrows(NuvexaEncryptionException::class.java) { NuvexaDatabase.open(path.toString()) }
        NuvexaDatabase.open(path.toString(), key).use { db -> assertCases(db, fixture) }
    }

    @Test
    fun extendedCatalogAndTransaction() {
        assertEquals(NuvexaLibrary.ABI_VERSION, NuvexaDatabase.abiVersion())
        val path = Files.createTempFile("nuvexa-jvm-ext-", ".nvx")
        Files.deleteIfExists(path)
        NuvexaDatabase.create(path.toString(), "key").use { db ->
            db.insert("users", """{"name":"Ada","age":36}""")
            db.insertMany("users", """[{"name":"Ben","age":12}]""")
            assertEquals(listOf("users"), db.listCollections())
            assertEquals(2, db.count("users"))
            assertEquals(1, db.count("users", """{"age":{"${'$'}gte":21}}"""))
            db.beginTransaction()
            db.insert("users", """{"name":"Zoe","age":40}""")
            db.rollback()
            assertEquals(2, db.count("users"))
            val note = Files.createTempFile("nuvexa-note-", ".txt")
            Files.writeString(note, "hello")
            val id = db.uploadFile("note.txt", note.toString(), 4)
            val dest = Files.createTempFile("nuvexa-out-", ".txt")
            Files.deleteIfExists(dest)
            assertTrue(db.downloadFile(id, dest.toString()))
            assertEquals("hello", Files.readString(dest))
        }
    }

    private fun seed(db: NuvexaDatabase, fixture: JSONObject, collection: String) {
        val docs = fixture.getJSONArray("documents")
        for (i in 0 until docs.length()) {
            db.insert(collection, docs.getJSONObject(i).toString())
        }
        val indexes = fixture.getJSONArray("indexes")
        for (i in 0 until indexes.length()) {
            val fields = indexes.getJSONArray(i)
            db.ensureIndex(collection, (0 until fields.length()).map { fields.getString(it) })
        }
    }

    private fun assertCases(db: NuvexaDatabase, fixture: JSONObject) {
        val cases = fixture.getJSONArray("cases")
        for (i in 0 until cases.length()) {
            val query = cases.getJSONObject(i)
            val names = db.execute(query.getString("nql")).map { it.field("name") }
            val expected = query.getJSONArray("expectNames").toStringList()
            assertEquals(expected, names, query.getString("name"))
        }
    }

    private fun JSONArray.toStringList(): List<String> =
        (0 until length()).map { getString(it) }

    private fun findCases(): Path {
        var dir = Path.of("").toAbsolutePath()
        repeat(8) {
            val candidate = dir.resolve("tests/interop/cases.json")
            if (Files.isRegularFile(candidate)) {
                return candidate
            }
            dir = dir.parent ?: return@repeat
        }
        throw IllegalStateException("tests/interop/cases.json was not found.")
    }
}
