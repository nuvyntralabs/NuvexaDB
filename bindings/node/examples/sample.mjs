import { unlink } from "node:fs/promises";
import { NuvexaDatabase, NuvexaEncryptionException } from "../src/index.js";


const path = process.argv[2] ?? "sample.nvx";
await unlink(path).catch(() => {});

const db = await NuvexaDatabase.create(path, "sample-key");
try {
  await db.insert("users", JSON.stringify({ name: "Ada", age: 36 }));
  await db.ensureIndex("users", "age");
  for (const row of await db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)")) {
    console.log(row.json);
  }
  console.log("collections", await db.listCollections());
} finally {
  await db.close();
}

console.log("encrypted", await NuvexaDatabase.isEncrypted(path));
try {
  await NuvexaDatabase.open(path);
  throw new Error("open without key should fail");
} catch (ex) {
  if (!(ex instanceof NuvexaEncryptionException)) {
    throw ex;
  }
  console.log("fail-closed", ex.message);
}
