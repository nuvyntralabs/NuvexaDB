import koffi from "koffi";
import { Status, throwStatus } from "./errors.js";

function loadNative(libraryPath) {
  const path = libraryPath || process.env.NUVEXA_NATIVE_LIB;
  if (!path) {
    throw new Error("Set NUVEXA_NATIVE_LIB to the published nuvexa shared library.");
  }
  const lib = koffi.load(path);
  return {
    abiVersion: lib.func("int nuvexa_abi_version()"),
    create: lib.func("int nuvexa_create(const char *path, const char *key, _Out_ intptr_t *handle)"),
    open: lib.func("int nuvexa_open(const char *path, const char *key, _Out_ intptr_t *handle)"),
    close: lib.func("int nuvexa_close(intptr_t handle)"),
    isEncrypted: lib.func("int nuvexa_is_encrypted(const char *path, _Out_ int *encrypted)"),
    insert: lib.func("int nuvexa_insert(intptr_t handle, const char *collection, const char *json, _Out_ void **id)"),
    insertMany: lib.func("int nuvexa_insert_many(intptr_t handle, const char *collection, const char *json, _Out_ void **ids)"),
    replace: lib.func("int nuvexa_replace(intptr_t handle, const char *collection, const char *json)"),
    deleteById: lib.func("int nuvexa_delete_by_id(intptr_t handle, const char *collection, const char *id, _Out_ int *deleted)"),
    findById: lib.func("int nuvexa_find_by_id(intptr_t handle, const char *collection, const char *id, _Out_ void **json)"),
    execute: lib.func("int nuvexa_execute(intptr_t handle, const char *nql, _Out_ void **json)"),
    ensureIndex: lib.func("int nuvexa_ensure_index(intptr_t handle, const char *collection, const char *fields)"),
    listCollections: lib.func("int nuvexa_list_collections(intptr_t handle, _Out_ void **json)"),
    dropCollection: lib.func("int nuvexa_drop_collection(intptr_t handle, const char *collection)"),
    renameCollection: lib.func("int nuvexa_rename_collection(intptr_t handle, const char *from, const char *to)"),
    listIndexes: lib.func("int nuvexa_list_indexes(intptr_t handle, const char *collection, _Out_ void **json)"),
    dropIndex: lib.func("int nuvexa_drop_index(intptr_t handle, const char *collection, const char *name)"),
    count: lib.func("int nuvexa_count(intptr_t handle, const char *collection, const char *filter, _Out_ int64_t *count)"),
    checkpoint: lib.func("int nuvexa_checkpoint(intptr_t handle)"),
    backup: lib.func("int nuvexa_backup(intptr_t handle, const char *dest)"),
    compact: lib.func("int nuvexa_compact(intptr_t handle)"),
    restore: lib.func("int nuvexa_restore(const char *backup, const char *dest, int overwrite)"),
    stats: lib.func("int nuvexa_stats(intptr_t handle, _Out_ void **json)"),
    changeEncryptionKey: lib.func("int nuvexa_change_encryption_key(intptr_t handle, const char *current, const char *next)"),
    beginTransaction: lib.func("int nuvexa_begin_transaction(intptr_t handle)"),
    commit: lib.func("int nuvexa_commit(intptr_t handle)"),
    rollback: lib.func("int nuvexa_rollback(intptr_t handle)"),
    fsUpload: lib.func("int nuvexa_fs_upload(intptr_t handle, const char *name, const char *source, int chunk, _Out_ void **id)"),
    fsDownload: lib.func("int nuvexa_fs_download(intptr_t handle, const char *id, const char *dest, _Out_ int *found)"),
    fsMetadata: lib.func("int nuvexa_fs_metadata(intptr_t handle, const char *id, _Out_ void **json)"),
    lastError: lib.func("int nuvexa_last_error(_Out_ void **message)"),
    free: lib.func("void nuvexa_free(void *pointer)"),
  };
}

let cached;

export function getLib(libraryPath) {
  if (libraryPath) {
    return loadNative(libraryPath);
  }
  cached ??= loadNative();
  return cached;
}

function lastError(lib) {
  const box = [null];
  lib.lastError(box);
  return takeString(lib, box) || "NuvexaDB native call failed.";
}

function check(lib, status) {
  if (status === Status.OK) {
    return;
  }
  throwStatus(status, lastError(lib));
}

function takeString(lib, box) {
  const value = box[0];
  if (!value) {
    return "";
  }
  if (typeof value === "string") {
    return value;
  }
  try {
    return koffi.decode(value, "char", -1);
  } finally {
    lib.free(value);
  }
}

export const ffiBridge = {
  create(path, key, libraryPath) {
    const lib = getLib(libraryPath);
    const handle = [0n];
    check(lib, lib.create(path, key ?? null, handle));
    return handle[0];
  },
  open(path, key, libraryPath) {
    const lib = getLib(libraryPath);
    const handle = [0n];
    check(lib, lib.open(path, key ?? null, handle));
    return handle[0];
  },
  close(handle, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.close(handle));
  },
  isEncrypted(path, libraryPath) {
    const lib = getLib(libraryPath);
    const flag = [0];
    check(lib, lib.isEncrypted(path, flag));
    return flag[0] !== 0;
  },
  insert(handle, collection, json, libraryPath) {
    const lib = getLib(libraryPath);
    const id = [null];
    check(lib, lib.insert(handle, collection, json, id));
    return takeString(lib, id);
  },
  replace(handle, collection, json, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.replace(handle, collection, json));
  },
  deleteById(handle, collection, id, libraryPath) {
    const lib = getLib(libraryPath);
    const deleted = [0];
    check(lib, lib.deleteById(handle, collection, id, deleted));
    return deleted[0] !== 0;
  },
  findById(handle, collection, id, libraryPath) {
    const lib = getLib(libraryPath);
    const json = [null];
    const status = lib.findById(handle, collection, id, json);
    if (status === Status.NOT_FOUND) {
      if (json[0]) {
        lib.free(json[0]);
      }
      return null;
    }
    check(lib, status);
    return takeString(lib, json);
  },
  execute(handle, nql, libraryPath) {
    const lib = getLib(libraryPath);
    const json = [null];
    check(lib, lib.execute(handle, nql, json));
    return takeString(lib, json);
  },
  ensureIndex(handle, collection, fieldsJson, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.ensureIndex(handle, collection, fieldsJson));
  },
  abiVersion(libraryPath) {
    return getLib(libraryPath).abiVersion();
  },
  insertMany(handle, collection, jsonArray, libraryPath) {
    const lib = getLib(libraryPath);
    const ids = [null];
    check(lib, lib.insertMany(handle, collection, jsonArray, ids));
    return takeString(lib, ids);
  },
  listCollections(handle, libraryPath) {
    const lib = getLib(libraryPath);
    const json = [null];
    check(lib, lib.listCollections(handle, json));
    return takeString(lib, json);
  },
  dropCollection(handle, collection, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.dropCollection(handle, collection));
  },
  renameCollection(handle, from, to, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.renameCollection(handle, from, to));
  },
  listIndexes(handle, collection, libraryPath) {
    const lib = getLib(libraryPath);
    const json = [null];
    check(lib, lib.listIndexes(handle, collection, json));
    return takeString(lib, json);
  },
  dropIndex(handle, collection, name, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.dropIndex(handle, collection, name));
  },
  count(handle, collection, filterJson, libraryPath) {
    const lib = getLib(libraryPath);
    const n = [0n];
    check(lib, lib.count(handle, collection, filterJson ?? "{}", n));
    return Number(n[0]);
  },
  checkpoint(handle, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.checkpoint(handle));
  },
  backup(handle, destPath, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.backup(handle, destPath));
  },
  compact(handle, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.compact(handle));
  },
  restore(backupPath, destPath, overwrite, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.restore(backupPath, destPath, overwrite ? 1 : 0));
  },
  stats(handle, libraryPath) {
    const lib = getLib(libraryPath);
    const json = [null];
    check(lib, lib.stats(handle, json));
    return takeString(lib, json);
  },
  changeEncryptionKey(handle, currentKey, nextKey, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.changeEncryptionKey(handle, currentKey, nextKey));
  },
  beginTransaction(handle, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.beginTransaction(handle));
  },
  commit(handle, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.commit(handle));
  },
  rollback(handle, libraryPath) {
    const lib = getLib(libraryPath);
    check(lib, lib.rollback(handle));
  },
  uploadFile(handle, fileName, sourcePath, chunkSize, libraryPath) {
    const lib = getLib(libraryPath);
    const id = [null];
    check(lib, lib.fsUpload(handle, fileName, sourcePath, chunkSize ?? 0, id));
    return takeString(lib, id);
  },
  downloadFile(handle, fileId, destPath, libraryPath) {
    const lib = getLib(libraryPath);
    const found = [0];
    check(lib, lib.fsDownload(handle, fileId, destPath, found));
    return found[0] !== 0;
  },
  fileMetadata(handle, fileId, libraryPath) {
    const lib = getLib(libraryPath);
    const json = [null];
    const status = lib.fsMetadata(handle, fileId, json);
    if (status === Status.NOT_FOUND) {
      if (json[0]) {
        lib.free(json[0]);
      }
      return null;
    }
    check(lib, status);
    return takeString(lib, json);
  },
};
