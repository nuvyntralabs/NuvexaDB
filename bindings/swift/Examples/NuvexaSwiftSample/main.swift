import Foundation
import NuvexaDB

let path = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
    .appendingPathComponent("sample.nvx").path
try? FileManager.default.removeItem(atPath: path)
let key = "sample-key"

let db = try NuvexaDatabase.create(path, key: key)
let adaId = try db.insert(
    collection: "users",
    json: #"{"name":"Ada","age":36,"status":"active","address":{"city":"London"}}"#)
_ = try db.insertMany(
    collection: "users",
    jsonArray: """
    [
      {"name":"Grace","age":85,"status":"retired","address":{"city":"New York"}},
      {"name":"Cara","age":21,"status":"active","address":{"city":"Bengaluru"}},
      {"name":"Alan","age":42,"status":"active","address":{"city":"London"}}
    ]
    """)
let scratchId = try db.insert(
    collection: "users",
    json: #"{"name":"Scratch","age":19,"status":"active","address":{"city":"Paris"}}"#)
try db.ensureIndex(collection: "users", field: "age")
try db.ensureIndex(collection: "users", fields: ["address.city", "status"])
print("Created collection users. Collections: \(try db.listCollections())")
print("Read Ada: \(try db.findById(collection: "users", id: adaId)?.json ?? "")")
try db.replace(
    collection: "users",
    json: "{\"_id\":\"\(adaId)\",\"name\":\"Ada Lovelace\",\"age\":36,\"status\":\"active\",\"address\":{\"city\":\"London\"}}")
print("Updated Ada: \(try db.findById(collection: "users", id: adaId)?.json ?? "")")
print("Deleted scratch: \(try db.deleteById(collection: "users", id: scratchId))")
print("-- NQL age >= 21, sort name, limit 10 --")
for row in try db.execute("db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)") {
    print(row.json)
}
print(#"-- NQL $and London + active --"#)
for row in try db.execute(
    #"db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })"#)
{
    print(row.json)
}
print(#"-- NQL $or age < 30 or New York --"#)
for row in try db.execute(
    #"db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })"#)
{
    print(row.json)
}
try db.close()

print("IsEncrypted: \(try NuvexaDatabase.isEncrypted(path))")
do {
    _ = try NuvexaDatabase.open(path)
    print("ERROR: open without key should have failed.")
} catch NuvexaError.encryption(let message) {
    print("Lib fail-closed: \(message)")
}
