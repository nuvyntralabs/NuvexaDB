import Foundation
import NuvexaDB

let path = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
    .appendingPathComponent("sample.nvx").path
try? FileManager.default.removeItem(atPath: path)
let key = "sample-key"

let db = try NuvexaDatabase.create(path, key: key)
_ = try db.insert(collection: "users", json: #"{"name":"Ada","age":36}"#)
try db.ensureIndex(collection: "users", field: "age")
for row in try db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)") {
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
