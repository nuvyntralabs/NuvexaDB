import { unlinkSync } from "node:fs";
import { NuvexaDatabase, NuvexaEncryptionException } from "../src/index.js";

const path = process.argv[2] ?? "sample.nvx";
try { unlinkSync(path); } catch { /* new file */ }

const db = await NuvexaDatabase.create(path, "sample-key");
await db.insert("users", JSON.stringify({ name: "Ada", age: 36 }));
await db.ensureIndex("users", "age");
for (const row of await db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)")) {
  console.log(row.json);
}
await db.close();

console.log("IsEncrypted:", await NuvexaDatabase.isEncrypted(path));
try {
  await NuvexaDatabase.open(path);
  console.log("ERROR: open without key should have failed.");
} catch (ex) {
  if (ex instanceof NuvexaEncryptionException) {
    console.log("Lib fail-closed:", ex.message);
  } else {
    throw ex;
  }
}
