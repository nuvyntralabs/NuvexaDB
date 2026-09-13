export class NuvexaException extends Error {}
export class NuvexaEncryptionException extends NuvexaException {}
export class NuvexaIntegrityException extends NuvexaException {}

export class NuvexaDocument {
  constructor(json: string | Record<string, unknown>);
  readonly json: string;
  readonly object: Record<string, unknown>;
  readonly id: string;
  field(name: string): string | null;
  static parse(json: string): NuvexaDocument;
}

export class NuvexaDatabase {
  static create(path: string, key?: string | null): Promise<NuvexaDatabase>;
  static open(path: string, key?: string | null): Promise<NuvexaDatabase>;
  static isEncrypted(path: string): Promise<boolean>;
  static abiVersion(): Promise<number>;
  static restore(backupPath: string, destPath: string, overwrite?: boolean): Promise<void>;
  insert(collection: string, json: string): Promise<string>;
  insertMany(collection: string, jsonArray: string): Promise<string[]>;
  replace(collection: string, json: string): Promise<void>;
  deleteById(collection: string, id: string): Promise<boolean>;
  findById(collection: string, id: string): Promise<NuvexaDocument | null>;
  execute(nql: string): Promise<NuvexaDocument[]>;
  ensureIndex(collection: string, fields: string | string[]): Promise<void>;
  listCollections(): Promise<string[]>;
  dropCollection(collection: string): Promise<void>;
  renameCollection(from: string, to: string): Promise<void>;
  listIndexes(collection: string): Promise<Array<{ name: string; fields: string[]; unique: boolean }>>;
  dropIndex(collection: string, name: string): Promise<void>;
  count(collection: string, filterJson?: string): Promise<number>;
  checkpoint(): Promise<void>;
  backup(destPath: string): Promise<void>;
  compact(): Promise<void>;
  stats(): Promise<Record<string, unknown>>;
  changeEncryptionKey(currentKey: string, nextKey: string): Promise<void>;
  beginTransaction(): Promise<void>;
  commit(): Promise<void>;
  rollback(): Promise<void>;
  uploadFile(fileName: string, sourcePath: string, chunkSize?: number): Promise<string>;
  downloadFile(fileId: string, destPath: string): Promise<boolean>;
  fileMetadata(fileId: string): Promise<NuvexaDocument | null>;
  close(): Promise<void>;
}
