import Foundation
import XCTest
@testable import NuvexaDB

final class InteropFixtureTests: XCTestCase {
    func testGoldenCases() throws {
        try XCTSkipIf(ProcessInfo.processInfo.environment["NUVEXA_NATIVE_DIR"] == nil, "Set NUVEXA_NATIVE_DIR to the published lib folder.")
        let fixture = try loadCases()
        let key = fixture.key
        let collection = fixture.collection
        let path = FileManager.default.temporaryDirectory
            .appendingPathComponent("nuvexa-swift-\(UUID().uuidString).nvx")
            .path
        defer { try? FileManager.default.removeItem(atPath: path) }

        let created = try NuvexaDatabase.create(path, key: key)
        try seed(created, fixture: fixture, collection: collection)
        try assertCases(created, fixture: fixture)
        try created.close()

        XCTAssertTrue(try NuvexaDatabase.isEncrypted(path))
        XCTAssertThrowsError(try NuvexaDatabase.open(path)) { error in
            guard case NuvexaError.encryption = error else {
                XCTFail("expected encryption error, got \(error)")
                return
            }
        }

        let opened = try NuvexaDatabase.open(path, key: key)
        try assertCases(opened, fixture: fixture)
        try opened.close()
    }

    func testExtendedCatalogAndTransaction() throws {
        try XCTSkipIf(ProcessInfo.processInfo.environment["NUVEXA_NATIVE_DIR"] == nil, "Set NUVEXA_NATIVE_DIR to the published lib folder.")
        XCTAssertEqual(NuvexaDatabase.abiVersion(), 2)
        let path = FileManager.default.temporaryDirectory
            .appendingPathComponent("nuvexa-swift-ext-\(UUID().uuidString).nvx")
            .path
        defer { try? FileManager.default.removeItem(atPath: path) }
        let db = try NuvexaDatabase.create(path, key: "key")
        defer { try? db.close() }
        _ = try db.insert(collection: "users", json: #"{"name":"Ada","age":36}"#)
        _ = try db.insertMany(collection: "users", jsonArray: #"[{"name":"Ben","age":12}]"#)
        XCTAssertEqual(try db.listCollections(), ["users"])
        XCTAssertEqual(try db.count(collection: "users"), 2)
        try db.beginTransaction()
        _ = try db.insert(collection: "users", json: #"{"name":"Zoe","age":40}"#)
        try db.rollback()
        XCTAssertEqual(try db.count(collection: "users"), 2)
    }

    private func seed(_ db: NuvexaDatabase, fixture: Fixture, collection: String) throws {
        for doc in fixture.documents {
            _ = try db.insert(collection: collection, json: doc)
        }
        for fields in fixture.indexes {
            try db.ensureIndex(collection: collection, fields: fields)
        }
    }

    private func assertCases(_ db: NuvexaDatabase, fixture: Fixture) throws {
        for query in fixture.cases {
            let names = try db.execute(query.nql).map { $0.field("name") ?? "" }
            XCTAssertEqual(names, query.expectNames, query.name)
        }
    }

    private func loadCases() throws -> Fixture {
        var dir = URL(fileURLWithPath: #filePath)
        for _ in 0..<8 {
            dir.deleteLastPathComponent()
            let candidate = dir.appendingPathComponent("tests/interop/cases.json")
            if FileManager.default.isReadableFile(atPath: candidate.path) {
                let data = try Data(contentsOf: candidate)
                return try JSONDecoder().decode(Fixture.self, from: data)
            }
        }
        throw NuvexaError.error("tests/interop/cases.json was not found.")
    }
}

private struct Fixture: Decodable {
    let key: String
    let collection: String
    let documents: [String]
    let indexes: [[String]]
    let cases: [QueryCase]

    enum CodingKeys: String, CodingKey {
        case key, collection, documents, indexes, cases
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        key = try container.decode(String.self, forKey: .key)
        collection = try container.decode(String.self, forKey: .collection)
        indexes = try container.decode([[String]].self, forKey: .indexes)
        cases = try container.decode([QueryCase].self, forKey: .cases)
        let rawDocs = try container.decode([RawDocument].self, forKey: .documents)
        documents = try rawDocs.map { doc in
            let data = try JSONSerialization.data(withJSONObject: [
                "name": doc.name,
                "age": doc.age,
                "city": doc.city
            ])
            return String(data: data, encoding: .utf8) ?? "{}"
        }
    }
}

private struct RawDocument: Decodable {
    let name: String
    let age: Int
    let city: String
}

private struct QueryCase: Decodable {
    let name: String
    let nql: String
    let expectNames: [String]
}
