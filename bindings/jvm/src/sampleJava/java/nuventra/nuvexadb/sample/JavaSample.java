package nuventra.nuvexadb.sample;

import nuventra.nuvexadb.NuvexaDatabase;
import nuventra.nuvexadb.NuvexaDocument;
import nuventra.nuvexadb.NuvexaEncryptionException;

import java.nio.file.Files;
import java.nio.file.Path;

/** Java sample inside the JVM library project. */
public final class JavaSample {
    public static void main(String[] args) throws Exception {
        Path path = Path.of(args.length > 0 ? args[0] : "sample.nvx");
        Files.deleteIfExists(path);
        String key = "sample-key";

        try (NuvexaDatabase db = NuvexaDatabase.create(path.toString(), key)) {
            db.insert("users", "{\"name\":\"Ada\",\"age\":36}");
            db.ensureIndex("users", "age");
            for (NuvexaDocument row : db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)")) {
                System.out.println(row);
            }
            System.out.println("collections " + db.listCollections());
        }

        System.out.println("IsEncrypted: " + NuvexaDatabase.isEncrypted(path.toString()));
        try {
            NuvexaDatabase.open(path.toString());
            throw new IllegalStateException("open without key should fail");
        } catch (NuvexaEncryptionException ex) {
            System.out.println("Lib fail-closed: " + ex.getMessage());
        }
    }
}
