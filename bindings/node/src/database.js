import { ffiBridge as bridge } from "./ffi.js";

export class NuvexaDocument {
  constructor(json) {
    this.json = typeof json === "string" ? json : JSON.stringify(json);
    this.object = typeof json === "string" ? JSON.parse(json) : json;
  }

  get id() {
    return this.object._id?.toString() ?? "";
  }

  field(name) {
    const value = this.object[name];
    return value == null ? null : String(value);
  }

  toString() {
    return this.json;
  }

  static parse(json) {
    return new NuvexaDocument(json);
  }
}

export class NuvexaDatabase {
  constructor(handle) {
    this._handle = handle;
  }

  static async create(path, key = null) {
    return new NuvexaDatabase(await Promise.resolve(bridge.create(path, key)));
  }

  static async open(path, key = null) {
    return new NuvexaDatabase(await Promise.resolve(bridge.open(path, key)));
  }

  static async isEncrypted(path) {
    return Promise.resolve(bridge.isEncrypted(path));
  }

  static async abiVersion() {
    return Promise.resolve(bridge.abiVersion());
  }

  static async restore(backupPath, destPath, overwrite = false) {
    await Promise.resolve(bridge.restore(backupPath, destPath, overwrite));
  }

  async insert(collection, json) {
    return Promise.resolve(bridge.insert(this._handle, collection, json));
  }

  async insertMany(collection, jsonArray) {
    const json = await Promise.resolve(bridge.insertMany(this._handle, collection, jsonArray));
    return JSON.parse(json || "[]");
  }

  async replace(collection, json) {
    await Promise.resolve(bridge.replace(this._handle, collection, json));
  }

  async deleteById(collection, id) {
    return Promise.resolve(bridge.deleteById(this._handle, collection, id));
  }

  async findById(collection, id) {
    const json = await Promise.resolve(bridge.findById(this._handle, collection, id));
    return json == null ? null : NuvexaDocument.parse(json);
  }

  async execute(nql) {
    const json = await Promise.resolve(bridge.execute(this._handle, nql));
    return JSON.parse(json || "[]").map((row) => new NuvexaDocument(row));
  }

  async ensureIndex(collection, fields) {
    const list = Array.isArray(fields) ? fields : [fields];
    await Promise.resolve(
      bridge.ensureIndex(this._handle, collection, JSON.stringify(list.length === 1 ? list[0] : list))
    );
  }

  async listCollections() {
    const json = await Promise.resolve(bridge.listCollections(this._handle));
    return JSON.parse(json || "[]");
  }

  async dropCollection(collection) {
    await Promise.resolve(bridge.dropCollection(this._handle, collection));
  }

  async renameCollection(from, to) {
    await Promise.resolve(bridge.renameCollection(this._handle, from, to));
  }

  async listIndexes(collection) {
    const json = await Promise.resolve(bridge.listIndexes(this._handle, collection));
    return JSON.parse(json || "[]");
  }

  async dropIndex(collection, name) {
    await Promise.resolve(bridge.dropIndex(this._handle, collection, name));
  }

  async count(collection, filterJson = "{}") {
    return Promise.resolve(bridge.count(this._handle, collection, filterJson));
  }

  async checkpoint() {
    await Promise.resolve(bridge.checkpoint(this._handle));
  }

  async backup(destPath) {
    await Promise.resolve(bridge.backup(this._handle, destPath));
  }

  async compact() {
    await Promise.resolve(bridge.compact(this._handle));
  }

  async stats() {
    const json = await Promise.resolve(bridge.stats(this._handle));
    return JSON.parse(json || "{}");
  }

  async changeEncryptionKey(currentKey, nextKey) {
    await Promise.resolve(bridge.changeEncryptionKey(this._handle, currentKey, nextKey));
  }

  async beginTransaction() {
    await Promise.resolve(bridge.beginTransaction(this._handle));
  }

  async commit() {
    await Promise.resolve(bridge.commit(this._handle));
  }

  async rollback() {
    await Promise.resolve(bridge.rollback(this._handle));
  }

  async uploadFile(fileName, sourcePath, chunkSize = 0) {
    return Promise.resolve(bridge.uploadFile(this._handle, fileName, sourcePath, chunkSize));
  }

  async downloadFile(fileId, destPath) {
    return Promise.resolve(bridge.downloadFile(this._handle, fileId, destPath));
  }

  async fileMetadata(fileId) {
    const json = await Promise.resolve(bridge.fileMetadata(this._handle, fileId));
    return json == null ? null : NuvexaDocument.parse(json);
  }

  async close() {
    if (this._handle == null) {
      return;
    }
    await Promise.resolve(bridge.close(this._handle));
    this._handle = null;
  }
}
