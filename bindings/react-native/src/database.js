import { ffiBridge } from "./ffi.js";

function nativeBridge() {
  try {
    const { NativeModules } = require("react-native");
    const module = NativeModules?.NuvexaDB;
    if (module) {
      return {
        create: (path, key) => module.create(path, key ?? null),
        open: (path, key) => module.open(path, key ?? null),
        close: (handle) => module.close(handle),
        isEncrypted: (path) => module.isEncrypted(path),
        insert: (handle, collection, json) => module.insert(handle, collection, json),
        replace: (handle, collection, json) => module.replace(handle, collection, json),
        deleteById: (handle, collection, id) => module.deleteById(handle, collection, id),
        findById: (handle, collection, id) => module.findById(handle, collection, id),
        execute: (handle, nql) => module.execute(handle, nql),
        ensureIndex: (handle, collection, fieldsJson) => module.ensureIndex(handle, collection, fieldsJson),
        abiVersion: () => module.abiVersion(),
        insertMany: (handle, collection, jsonArray) => module.insertMany(handle, collection, jsonArray),
        listCollections: (handle) => module.listCollections(handle),
        dropCollection: (handle, collection) => module.dropCollection(handle, collection),
        renameCollection: (handle, from, to) => module.renameCollection(handle, from, to),
        listIndexes: (handle, collection) => module.listIndexes(handle, collection),
        dropIndex: (handle, collection, name) => module.dropIndex(handle, collection, name),
        count: (handle, collection, filterJson) => module.count(handle, collection, filterJson),
        checkpoint: (handle) => module.checkpoint(handle),
        backup: (handle, destPath) => module.backup(handle, destPath),
        compact: (handle) => module.compact(handle),
        restore: (backupPath, destPath, overwrite) => module.restore(backupPath, destPath, overwrite),
        stats: (handle) => module.stats(handle),
        changeEncryptionKey: (handle, currentKey, nextKey) => module.changeEncryptionKey(handle, currentKey, nextKey),
        beginTransaction: (handle) => module.beginTransaction(handle),
        commit: (handle) => module.commit(handle),
        rollback: (handle) => module.rollback(handle),
        uploadFile: (handle, fileName, sourcePath, chunkSize) => module.uploadFile(handle, fileName, sourcePath, chunkSize),
        downloadFile: (handle, fileId, destPath) => module.downloadFile(handle, fileId, destPath),
        fileMetadata: (handle, fileId) => module.fileMetadata(handle, fileId),
      };
    }
  } catch {
    // Node / Jest use koffi against the published C ABI.
  }
  return ffiBridge;
}

const bridge = nativeBridge();

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

  async insert(collection, json) {
    return Promise.resolve(bridge.insert(this._handle, collection, json));
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

  static async abiVersion() {
    return Promise.resolve(bridge.abiVersion());
  }

  static async restore(backupPath, destPath, overwrite = false) {
    await Promise.resolve(bridge.restore(backupPath, destPath, overwrite));
  }

  async insertMany(collection, jsonArray) {
    const json = await Promise.resolve(bridge.insertMany(this._handle, collection, jsonArray));
    return JSON.parse(json || "[]");
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
