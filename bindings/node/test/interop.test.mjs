import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { NuvexaDatabase, NuvexaEncryptionException } from "../src/index.js";

function findCases() {
  let dir = fileURLToPath(new URL(".", import.meta.url));
  for (let i = 0; i < 8; i++) {
    const candidate = join(dir, "tests/interop/cases.json");
    try {
      return JSON.parse(readFileSync(candidate, "utf8"));
    } catch {
      dir = join(dir, "..");
    }
  }
  throw new Error("tests/interop/cases.json was not found.");
}

async function seed(db, fixture) {
  for (const doc of fixture.documents) {
    await db.insert(fixture.collection, JSON.stringify(doc));
  }
  for (const fields of fixture.indexes) {
    await db.ensureIndex(fixture.collection, fields);
  }
}

async function assertCases(db, fixture) {
  for (const query of fixture.cases) {
    const names = (await db.execute(query.nql)).map((doc) => doc.field("name"));
    assert.deepEqual(names, query.expectNames, query.name);
  }
}

test("golden NQL cases", { skip: !process.env.NUVEXA_NATIVE_LIB }, async () => {
  const fixture = findCases();
  const path = join(tmpdir(), `nuvexa-node-${Date.now()}.nvx`);
  const created = await NuvexaDatabase.create(path, fixture.key);
  try {
    await seed(created, fixture);
    await assertCases(created, fixture);
  } finally {
    await created.close();
  }

  assert.equal(await NuvexaDatabase.isEncrypted(path), true);
  await assert.rejects(() => NuvexaDatabase.open(path), NuvexaEncryptionException);

  const opened = await NuvexaDatabase.open(path, fixture.key);
  try {
    await assertCases(opened, fixture);
  } finally {
    await opened.close();
  }
});

test("extended catalog and transaction", { skip: !process.env.NUVEXA_NATIVE_LIB }, async () => {
  assert.equal(await NuvexaDatabase.abiVersion(), 2);
  const path = join(tmpdir(), `nuvexa-node-ext-${Date.now()}.nvx`);
  const db = await NuvexaDatabase.create(path, "key");
  try {
    await db.insert("users", JSON.stringify({ name: "Ada", age: 36 }));
    await db.insertMany("users", JSON.stringify([{ name: "Ben", age: 12 }]));
    assert.deepEqual(await db.listCollections(), ["users"]);
    assert.equal(await db.count("users"), 2);
    await db.beginTransaction();
    await db.insert("users", JSON.stringify({ name: "Zoe", age: 40 }));
    await db.rollback();
    assert.equal(await db.count("users"), 2);
  } finally {
    await db.close();
  }
});
