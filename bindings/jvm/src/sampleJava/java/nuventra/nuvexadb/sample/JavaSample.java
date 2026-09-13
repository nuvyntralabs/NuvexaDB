package nuventra.nuvexadb.sample;

import nuventra.nuvexadb.NuvexaDatabase;
import nuventra.nuvexadb.NuvexaDocument;
import nuventra.nuvexadb.NuvexaEncryptionException;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;

/** Java sample inside the JVM library project. */
public final class JavaSample {
    public static void main(String[] args) throws Exception {
        Path path = Path.of(args.length > 0 ? args[0] : "sample.nvx");
        Files.deleteIfExists(path);
        String key = "sample-key";

        try (NuvexaDatabase db = NuvexaDatabase.create(path.toString(), key)) {
            String adaId = db.insert(
                "users",
                "{\"name\":\"Ada\",\"age\":36,\"status\":\"active\",\"address\":{\"city\":\"London\"}}");
            db.insertMany(
                "users",
                "["
                    + "{\"name\":\"Grace\",\"age\":85,\"status\":\"retired\",\"address\":{\"city\":\"New York\"}},"
                    + "{\"name\":\"Cara\",\"age\":21,\"status\":\"active\",\"address\":{\"city\":\"Bengaluru\"}},"
                    + "{\"name\":\"Alan\",\"age\":42,\"status\":\"active\",\"address\":{\"city\":\"London\"}}"
                    + "]");
            String scratchId = db.insert(
                "users",
                "{\"name\":\"Scratch\",\"age\":19,\"status\":\"active\",\"address\":{\"city\":\"Paris\"}}");
            db.ensureIndex("users", "age");
            db.ensureIndex("users", List.of("address.city", "status"));
            System.out.println("Created collection users. Collections: " + db.listCollections());

            System.out.println("Read Ada: " + db.findById("users", adaId));
            db.replace(
                "users",
                "{\"_id\":\"" + adaId
                    + "\",\"name\":\"Ada Lovelace\",\"age\":36,\"status\":\"active\",\"address\":{\"city\":\"London\"}}");
            System.out.println("Updated Ada: " + db.findById("users", adaId));
            System.out.println("Deleted scratch: " + db.deleteById("users", scratchId));

            System.out.println("-- NQL age >= 21, sort name, limit 10 --");
            for (NuvexaDocument row : db.execute("db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)")) {
                System.out.println(row);
            }
            System.out.println("-- NQL $and London + active --");
            for (NuvexaDocument row : db.execute(
                "db.users.find({ $and: [ { \"address.city\": \"London\" }, { status: \"active\" } ] }).sort({ age: -1 })")) {
                System.out.println(row);
            }
            System.out.println("-- NQL $or age < 30 or New York --");
            for (NuvexaDocument row : db.execute(
                "db.users.find({ $or: [ { age: { $lt: 30 } }, { \"address.city\": \"New York\" } ] })")) {
                System.out.println(row);
            }
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
