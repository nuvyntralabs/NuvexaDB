import { unlink } from "node:fs/promises";
import { NuvexaDatabase, NuvexaEncryptionException } from "../src/index.js";

const path = process.argv[2] ?? "sample.nvx";
await unlink(path).catch(() => {});

const db = await NuvexaDatabase.create(path, "sample-key");
try {
  const adaId = await db.insert(
    "users",
    JSON.stringify({ name: "Ada", age: 36, status: "active", address: { city: "London" } })
  );
  await db.insertMany(
    "users",
    JSON.stringify([
      { name: "Grace", age: 85, status: "retired", address: { city: "New York" } },
      { name: "Cara", age: 21, status: "active", address: { city: "Bengaluru" } },
      { name: "Alan", age: 42, status: "active", address: { city: "London" } }
    ])
  );
  const scratchId = await db.insert(
    "users",
    JSON.stringify({ name: "Scratch", age: 19, status: "active", address: { city: "Paris" } })
  );
  await db.ensureIndex("users", "age");
  await db.ensureIndex("users", ["address.city", "status"]);
  console.log("Created collection users. Collections", await db.listCollections());
  console.log("Read Ada:", (await db.findById("users", adaId))?.json);
  await db.replace(
    "users",
    JSON.stringify({
      _id: adaId,
      name: "Ada Lovelace",
      age: 36,
      status: "active",
      address: { city: "London" }
    })
  );
  console.log("Updated Ada:", (await db.findById("users", adaId))?.json);
  console.log("Deleted scratch:", await db.deleteById("users", scratchId));
  console.log("-- NQL age >= 21, sort name, limit 10 --");
  for (const row of await db.execute("db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)")) {
    console.log(row.json);
  }
  console.log("-- NQL $and London + active --");
  for (const row of await db.execute(
    'db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })'
  )) {
    console.log(row.json);
  }
  console.log("-- NQL $or age < 30 or New York --");
  for (const row of await db.execute(
    'db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })'
  )) {
    console.log(row.json);
  }
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
