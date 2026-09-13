import CNuvexa
import Foundation

public struct NuvexaDocument {
    public let json: String
    public let object: [String: Any]

    public var id: String { object["_id"] as? String ?? "" }

    public func field(_ name: String) -> String? {
        object[name].map { String(describing: $0) }
    }

    public static func parse(_ json: String) throws -> NuvexaDocument {
        guard let data = json.data(using: .utf8),
              let object = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw NuvexaError.error("A NuvexaDB document must be a JSON object.")
        }
        return NuvexaDocument(json: json, object: object)
    }
}

public enum NuvexaError: Error, LocalizedError {
    case error(String)
    case encryption(String)
    case integrity(String)
    case notFound(String)

    public var errorDescription: String? {
        switch self {
        case .error(let m), .encryption(let m), .integrity(let m), .notFound(let m):
            return m
        }
    }

    static func from(status: Int32) -> NuvexaError {
        let message = nuvexaLastError()
        switch status {
        case Int32(NUVEXA_ENCRYPTION): return .encryption(message)
        case Int32(NUVEXA_INTEGRITY): return .integrity(message)
        case Int32(NUVEXA_NOT_FOUND): return .notFound(message)
        default: return .error(message)
        }
    }
}

public final class NuvexaDatabase {
    private var handle: nuvexa_handle

    public static func isEncrypted(_ path: String) throws -> Bool {
        var flag: Int32 = 0
        try path.withCString { cPath in
            try Self.check(nuvexa_is_encrypted(cPath, &flag))
        }
        return flag != 0
    }

    public static func create(_ path: String, key: String? = nil) throws -> NuvexaDatabase {
        try openOrCreate(path, key: key, create: true)
    }

    public static func open(_ path: String, key: String? = nil) throws -> NuvexaDatabase {
        try openOrCreate(path, key: key, create: false)
    }

    private static func openOrCreate(_ path: String, key: String?, create: Bool) throws -> NuvexaDatabase {
        var handle: nuvexa_handle = 0
        let status: Int32 = path.withCString { cPath in
            withOptionalCString(key) { cKey in
                create ? nuvexa_create(cPath, cKey, &handle) : nuvexa_open(cPath, cKey, &handle)
            }
        }
        try check(status)
        return NuvexaDatabase(handle: handle)
    }

    private init(handle: nuvexa_handle) {
        self.handle = handle
    }

    deinit {
        if handle != 0 {
            _ = nuvexa_close(handle)
        }
    }

    public func close() throws {
        guard handle != 0 else { return }
        let status = nuvexa_close(handle)
        handle = 0
        try Self.check(status)
    }

    @discardableResult
    public func insert(collection: String, json: String) throws -> String {
        var idPtr: UnsafeMutablePointer<CChar>?
        let status = collection.withCString { cCol in
            json.withCString { cJson in
                nuvexa_insert(handle, cCol, cJson, &idPtr)
            }
        }
        try Self.check(status)
        return nuvexaTakeString(&idPtr)
    }

    public func replace(collection: String, json: String) throws {
        let status = collection.withCString { cCol in
            json.withCString { cJson in
                nuvexa_replace(handle, cCol, cJson)
            }
        }
        try Self.check(status)
    }

    @discardableResult
    public func deleteById(collection: String, id: String) throws -> Bool {
        var deleted: Int32 = 0
        let status = collection.withCString { cCol in
            id.withCString { cId in
                nuvexa_delete_by_id(handle, cCol, cId, &deleted)
            }
        }
        try Self.check(status)
        return deleted != 0
    }

    public func findById(collection: String, id: String) throws -> NuvexaDocument? {
        var jsonPtr: UnsafeMutablePointer<CChar>?
        let status = collection.withCString { cCol in
            id.withCString { cId in
                nuvexa_find_by_id(handle, cCol, cId, &jsonPtr)
            }
        }
        if status == Int32(NUVEXA_NOT_FOUND) {
            if let jsonPtr { nuvexa_free(jsonPtr) }
            return nil
        }
        try Self.check(status)
        return try NuvexaDocument.parse(nuvexaTakeString(&jsonPtr))
    }

    public func execute(_ nql: String) throws -> [NuvexaDocument] {
        var jsonPtr: UnsafeMutablePointer<CChar>?
        let status = nql.withCString { nuvexa_execute(handle, $0, &jsonPtr) }
        try Self.check(status)
        let json = nuvexaTakeString(&jsonPtr)
        guard let data = json.data(using: .utf8),
              let array = try JSONSerialization.jsonObject(with: data) as? [[String: Any]] else {
            return []
        }
        return try array.map { object in
            let payload = try String(data: JSONSerialization.data(withJSONObject: object), encoding: .utf8)
                ?? "{}"
            return NuvexaDocument(json: payload, object: object)
        }
    }

    public func ensureIndex(collection: String, fields: [String]) throws {
        let payload = try String(data: JSONSerialization.data(withJSONObject: fields), encoding: .utf8) ?? "[]"
        let status = collection.withCString { cCol in
            payload.withCString { cFields in
                nuvexa_ensure_index(handle, cCol, cFields)
            }
        }
        try Self.check(status)
    }

    public func ensureIndex(collection: String, field: String) throws {
        try ensureIndex(collection: collection, fields: [field])
    }

    public static func abiVersion() -> Int32 {
        nuvexa_abi_version()
    }

    public static func restore(backupPath: String, destPath: String, overwrite: Bool = false) throws {
        let status = backupPath.withCString { cBackup in
            destPath.withCString { cDest in
                nuvexa_restore(cBackup, cDest, overwrite ? 1 : 0)
            }
        }
        try check(status)
    }

    public func insertMany(collection: String, jsonArray: String) throws -> [String] {
        var idsPtr: UnsafeMutablePointer<CChar>?
        let status = collection.withCString { cCol in
            jsonArray.withCString { cJson in
                nuvexa_insert_many(handle, cCol, cJson, &idsPtr)
            }
        }
        try Self.check(status)
        let json = nuvexaTakeString(&idsPtr)
        guard let data = json.data(using: .utf8),
              let array = try JSONSerialization.jsonObject(with: data) as? [String] else {
            return []
        }
        return array
    }

    public func listCollections() throws -> [String] {
        var jsonPtr: UnsafeMutablePointer<CChar>?
        try Self.check(nuvexa_list_collections(handle, &jsonPtr))
        let json = nuvexaTakeString(&jsonPtr)
        guard let data = json.data(using: .utf8),
              let array = try JSONSerialization.jsonObject(with: data) as? [String] else {
            return []
        }
        return array
    }

    public func dropCollection(_ collection: String) throws {
        let status = collection.withCString { nuvexa_drop_collection(handle, $0) }
        try Self.check(status)
    }

    public func renameCollection(from: String, to: String) throws {
        let status = from.withCString { cFrom in
            to.withCString { cTo in
                nuvexa_rename_collection(handle, cFrom, cTo)
            }
        }
        try Self.check(status)
    }

    public func listIndexes(collection: String) throws -> [[String: Any]] {
        var jsonPtr: UnsafeMutablePointer<CChar>?
        let status = collection.withCString { nuvexa_list_indexes(handle, $0, &jsonPtr) }
        try Self.check(status)
        let json = nuvexaTakeString(&jsonPtr)
        guard let data = json.data(using: .utf8),
              let array = try JSONSerialization.jsonObject(with: data) as? [[String: Any]] else {
            return []
        }
        return array
    }

    public func dropIndex(collection: String, name: String) throws {
        let status = collection.withCString { cCol in
            name.withCString { cName in
                nuvexa_drop_index(handle, cCol, cName)
            }
        }
        try Self.check(status)
    }

    public func count(collection: String, filterJson: String = "{}") throws -> Int64 {
        var n: Int64 = 0
        let status = collection.withCString { cCol in
            filterJson.withCString { cFilter in
                nuvexa_count(handle, cCol, cFilter, &n)
            }
        }
        try Self.check(status)
        return n
    }

    public func checkpoint() throws {
        try Self.check(nuvexa_checkpoint(handle))
    }

    public func backup(destPath: String) throws {
        let status = destPath.withCString { nuvexa_backup(handle, $0) }
        try Self.check(status)
    }

    public func compact() throws {
        try Self.check(nuvexa_compact(handle))
    }

    public func stats() throws -> [String: Any] {
        var jsonPtr: UnsafeMutablePointer<CChar>?
        try Self.check(nuvexa_stats(handle, &jsonPtr))
        let json = nuvexaTakeString(&jsonPtr)
        guard let data = json.data(using: .utf8),
              let object = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return [:]
        }
        return object
    }

    public func changeEncryptionKey(currentKey: String, nextKey: String) throws {
        let status = currentKey.withCString { cCurrent in
            nextKey.withCString { cNext in
                nuvexa_change_encryption_key(handle, cCurrent, cNext)
            }
        }
        try Self.check(status)
    }

    public func beginTransaction() throws {
        try Self.check(nuvexa_begin_transaction(handle))
    }

    public func commit() throws {
        try Self.check(nuvexa_commit(handle))
    }

    public func rollback() throws {
        try Self.check(nuvexa_rollback(handle))
    }

    public func uploadFile(fileName: String, sourcePath: String, chunkSize: Int32 = 0) throws -> String {
        var idPtr: UnsafeMutablePointer<CChar>?
        let status = fileName.withCString { cName in
            sourcePath.withCString { cPath in
                nuvexa_fs_upload(handle, cName, cPath, chunkSize, &idPtr)
            }
        }
        try Self.check(status)
        return nuvexaTakeString(&idPtr)
    }

    public func downloadFile(fileId: String, destPath: String) throws -> Bool {
        var found: Int32 = 0
        let status = fileId.withCString { cId in
            destPath.withCString { cDest in
                nuvexa_fs_download(handle, cId, cDest, &found)
            }
        }
        try Self.check(status)
        return found != 0
    }

    public func fileMetadata(fileId: String) throws -> NuvexaDocument? {
        var jsonPtr: UnsafeMutablePointer<CChar>?
        let status = fileId.withCString { nuvexa_fs_metadata(handle, $0, &jsonPtr) }
        if status == Int32(NUVEXA_NOT_FOUND) {
            if let jsonPtr { nuvexa_free(jsonPtr) }
            return nil
        }
        try Self.check(status)
        return try NuvexaDocument.parse(nuvexaTakeString(&jsonPtr))
    }

    private static func check(_ status: Int32) throws {
        if status != Int32(NUVEXA_OK) {
            throw NuvexaError.from(status: status)
        }
    }

}

private func nuvexaLastError() -> String {
    var ptr: UnsafeMutablePointer<CChar>?
    _ = nuvexa_last_error(&ptr)
    return nuvexaTakeString(&ptr)
}

private func nuvexaTakeString(_ pointer: inout UnsafeMutablePointer<CChar>?) -> String {
    guard let pointer else { return "" }
    defer { nuvexa_free(pointer) }
    return String(cString: pointer)
}

private func withOptionalCString<T>(_ value: String?, _ body: (UnsafePointer<CChar>?) -> T) -> T {
    guard let value else {
        return body(nil)
    }
    return value.withCString(body)
}
