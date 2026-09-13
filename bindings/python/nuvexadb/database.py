from __future__ import annotations

import ctypes
import json
import os
from ctypes import (
    POINTER,
    c_char_p,
    c_int32,
    c_int64,
    c_void_p,
)
from typing import Any


class NuvexaException(Exception):
    pass


class NuvexaEncryptionException(NuvexaException):
    pass


class NuvexaIntegrityException(NuvexaException):
    pass


OK, ERROR, ENCRYPTION, INTEGRITY, NOT_FOUND = 0, 1, 2, 3, 4
ABI_VERSION = 2


def _load(path: str | None = None) -> ctypes.CDLL:
    library = path or os.environ.get("NUVEXA_NATIVE_LIB")
    if not library:
        raise NuvexaException("Set NUVEXA_NATIVE_LIB to the published nuvexa shared library.")
    return ctypes.CDLL(library)


class NuvexaDocument:
    def __init__(self, json_text: str | dict[str, Any]):
        if isinstance(json_text, str):
            self.json = json_text
            self.object = json.loads(json_text)
        else:
            self.object = json_text
            self.json = json.dumps(json_text)

    @property
    def id(self) -> str:
        return str(self.object.get("_id", ""))

    def field(self, name: str) -> str | None:
        value = self.object.get(name)
        return None if value is None else str(value)

    @staticmethod
    def parse(json_text: str) -> NuvexaDocument:
        return NuvexaDocument(json_text)


class NuvexaDatabase:
    def __init__(self, lib: ctypes.CDLL, handle: int):
        self._lib = lib
        self._handle = handle

    @staticmethod
    def abi_version(library: str | None = None) -> int:
        return int(_bind(_load(library)).nuvexa_abi_version())

    @classmethod
    def create(cls, path: str, key: str | None = None, library: str | None = None) -> NuvexaDatabase:
        return cls._open_or_create(path, key, create=True, library=library)

    @classmethod
    def open(cls, path: str, key: str | None = None, library: str | None = None) -> NuvexaDatabase:
        return cls._open_or_create(path, key, create=False, library=library)

    @classmethod
    def is_encrypted(cls, path: str, library: str | None = None) -> bool:
        lib = _bind(_load(library))
        flag = c_int32()
        _check(lib, lib.nuvexa_is_encrypted(path.encode(), ctypes.byref(flag)))
        return flag.value != 0

    @classmethod
    def restore(cls, backup_path: str, dest_path: str, overwrite: bool = False, library: str | None = None) -> None:
        lib = _bind(_load(library))
        _check(lib, lib.nuvexa_restore(backup_path.encode(), dest_path.encode(), 1 if overwrite else 0))

    @classmethod
    def _open_or_create(cls, path: str, key: str | None, create: bool, library: str | None) -> NuvexaDatabase:
        lib = _bind(_load(library))
        handle = c_void_p()
        fn = lib.nuvexa_create if create else lib.nuvexa_open
        _check(lib, fn(path.encode(), key.encode() if key else None, ctypes.byref(handle)))
        return cls(lib, handle.value or 0)

    def insert(self, collection: str, document: str) -> str:
        return self._string_out(self._lib.nuvexa_insert, collection, document)

    def insert_many(self, collection: str, json_array: str) -> list[str]:
        return json.loads(self._string_out(self._lib.nuvexa_insert_many, collection, json_array) or "[]")

    def replace(self, collection: str, document: str) -> None:
        _check(self._lib, self._lib.nuvexa_replace(self._handle, collection.encode(), document.encode()))

    def delete_by_id(self, collection: str, doc_id: str) -> bool:
        deleted = c_int32()
        _check(
            self._lib,
            self._lib.nuvexa_delete_by_id(self._handle, collection.encode(), doc_id.encode(), ctypes.byref(deleted)),
        )
        return deleted.value != 0

    def find_by_id(self, collection: str, doc_id: str) -> NuvexaDocument | None:
        out = c_void_p()
        status = self._lib.nuvexa_find_by_id(self._handle, collection.encode(), doc_id.encode(), ctypes.byref(out))
        if status == NOT_FOUND:
            _free(self._lib, out)
            return None
        _check(self._lib, status, out)
        return NuvexaDocument.parse(_take(self._lib, out))

    def execute(self, nql: str) -> list[NuvexaDocument]:
        rows = json.loads(self._nql(nql) or "[]")
        return [NuvexaDocument(row) for row in rows]

    def ensure_index(self, collection: str, fields: str | list[str]) -> None:
        payload = json.dumps(fields[0] if isinstance(fields, list) and len(fields) == 1 else fields)
        _check(self._lib, self._lib.nuvexa_ensure_index(self._handle, collection.encode(), payload.encode()))

    def list_collections(self) -> list[str]:
        out = c_void_p()
        _check(self._lib, self._lib.nuvexa_list_collections(self._handle, ctypes.byref(out)), out)
        return json.loads(_take(self._lib, out) or "[]")

    def drop_collection(self, collection: str) -> None:
        _check(self._lib, self._lib.nuvexa_drop_collection(self._handle, collection.encode()))

    def rename_collection(self, source: str, dest: str) -> None:
        _check(self._lib, self._lib.nuvexa_rename_collection(self._handle, source.encode(), dest.encode()))

    def list_indexes(self, collection: str) -> list[dict[str, Any]]:
        out = c_void_p()
        _check(self._lib, self._lib.nuvexa_list_indexes(self._handle, collection.encode(), ctypes.byref(out)), out)
        return json.loads(_take(self._lib, out) or "[]")

    def drop_index(self, collection: str, name: str) -> None:
        _check(self._lib, self._lib.nuvexa_drop_index(self._handle, collection.encode(), name.encode()))

    def count(self, collection: str, filter_json: str = "{}") -> int:
        n = c_int64()
        _check(
            self._lib,
            self._lib.nuvexa_count(self._handle, collection.encode(), filter_json.encode(), ctypes.byref(n)),
        )
        return int(n.value)

    def checkpoint(self) -> None:
        _check(self._lib, self._lib.nuvexa_checkpoint(self._handle))

    def backup(self, dest_path: str) -> None:
        _check(self._lib, self._lib.nuvexa_backup(self._handle, dest_path.encode()))

    def compact(self) -> None:
        _check(self._lib, self._lib.nuvexa_compact(self._handle))

    def stats(self) -> dict[str, Any]:
        out = c_void_p()
        _check(self._lib, self._lib.nuvexa_stats(self._handle, ctypes.byref(out)), out)
        return json.loads(_take(self._lib, out) or "{}")

    def change_encryption_key(self, current_key: str, next_key: str) -> None:
        _check(
            self._lib,
            self._lib.nuvexa_change_encryption_key(self._handle, current_key.encode(), next_key.encode()),
        )

    def begin_transaction(self) -> None:
        _check(self._lib, self._lib.nuvexa_begin_transaction(self._handle))

    def commit(self) -> None:
        _check(self._lib, self._lib.nuvexa_commit(self._handle))

    def rollback(self) -> None:
        _check(self._lib, self._lib.nuvexa_rollback(self._handle))

    def upload_file(self, file_name: str, source_path: str, chunk_size: int = 0) -> str:
        out = c_void_p()
        _check(
            self._lib,
            self._lib.nuvexa_fs_upload(
                self._handle, file_name.encode(), source_path.encode(), chunk_size, ctypes.byref(out)
            ),
            out,
        )
        return _take(self._lib, out)

    def download_file(self, file_id: str, dest_path: str) -> bool:
        found = c_int32()
        _check(
            self._lib,
            self._lib.nuvexa_fs_download(self._handle, file_id.encode(), dest_path.encode(), ctypes.byref(found)),
        )
        return found.value != 0

    def file_metadata(self, file_id: str) -> NuvexaDocument | None:
        out = c_void_p()
        status = self._lib.nuvexa_fs_metadata(self._handle, file_id.encode(), ctypes.byref(out))
        if status == NOT_FOUND:
            _free(self._lib, out)
            return None
        _check(self._lib, status, out)
        return NuvexaDocument.parse(_take(self._lib, out))

    def close(self) -> None:
        if self._handle == 0:
            return
        handle = self._handle
        self._handle = 0
        _check(self._lib, self._lib.nuvexa_close(handle))

    def __enter__(self) -> NuvexaDatabase:
        return self

    def __exit__(self, *_args: object) -> None:
        self.close()

    def _string_out(self, fn: Any, collection: str, payload: str) -> str:
        out = c_void_p()
        _check(self._lib, fn(self._handle, collection.encode(), payload.encode(), ctypes.byref(out)), out)
        return _take(self._lib, out)

    def _nql(self, nql: str) -> str:
        out = c_void_p()
        _check(self._lib, self._lib.nuvexa_execute(self._handle, nql.encode(), ctypes.byref(out)), out)
        return _take(self._lib, out)


def _bind(lib: ctypes.CDLL) -> ctypes.CDLL:
    lib.nuvexa_abi_version.restype = c_int32
    lib.nuvexa_last_error.argtypes = [POINTER(c_void_p)]
    lib.nuvexa_last_error.restype = c_int32
    lib.nuvexa_free.argtypes = [c_void_p]
    lib.nuvexa_free.restype = None
    lib.nuvexa_create.argtypes = [c_char_p, c_char_p, POINTER(c_void_p)]
    lib.nuvexa_open.argtypes = [c_char_p, c_char_p, POINTER(c_void_p)]
    lib.nuvexa_close.argtypes = [c_void_p]
    lib.nuvexa_is_encrypted.argtypes = [c_char_p, POINTER(c_int32)]
    lib.nuvexa_insert.argtypes = [c_void_p, c_char_p, c_char_p, POINTER(c_void_p)]
    lib.nuvexa_insert_many.argtypes = [c_void_p, c_char_p, c_char_p, POINTER(c_void_p)]
    lib.nuvexa_replace.argtypes = [c_void_p, c_char_p, c_char_p]
    lib.nuvexa_delete_by_id.argtypes = [c_void_p, c_char_p, c_char_p, POINTER(c_int32)]
    lib.nuvexa_find_by_id.argtypes = [c_void_p, c_char_p, c_char_p, POINTER(c_void_p)]
    lib.nuvexa_execute.argtypes = [c_void_p, c_char_p, POINTER(c_void_p)]
    lib.nuvexa_ensure_index.argtypes = [c_void_p, c_char_p, c_char_p]
    lib.nuvexa_list_collections.argtypes = [c_void_p, POINTER(c_void_p)]
    lib.nuvexa_drop_collection.argtypes = [c_void_p, c_char_p]
    lib.nuvexa_rename_collection.argtypes = [c_void_p, c_char_p, c_char_p]
    lib.nuvexa_list_indexes.argtypes = [c_void_p, c_char_p, POINTER(c_void_p)]
    lib.nuvexa_drop_index.argtypes = [c_void_p, c_char_p, c_char_p]
    lib.nuvexa_count.argtypes = [c_void_p, c_char_p, c_char_p, POINTER(c_int64)]
    lib.nuvexa_checkpoint.argtypes = [c_void_p]
    lib.nuvexa_backup.argtypes = [c_void_p, c_char_p]
    lib.nuvexa_compact.argtypes = [c_void_p]
    lib.nuvexa_restore.argtypes = [c_char_p, c_char_p, c_int32]
    lib.nuvexa_stats.argtypes = [c_void_p, POINTER(c_void_p)]
    lib.nuvexa_change_encryption_key.argtypes = [c_void_p, c_char_p, c_char_p]
    lib.nuvexa_begin_transaction.argtypes = [c_void_p]
    lib.nuvexa_commit.argtypes = [c_void_p]
    lib.nuvexa_rollback.argtypes = [c_void_p]
    lib.nuvexa_fs_upload.argtypes = [c_void_p, c_char_p, c_char_p, c_int32, POINTER(c_void_p)]
    lib.nuvexa_fs_download.argtypes = [c_void_p, c_char_p, c_char_p, POINTER(c_int32)]
    lib.nuvexa_fs_metadata.argtypes = [c_void_p, c_char_p, POINTER(c_void_p)]
    for name in (
        "nuvexa_create",
        "nuvexa_open",
        "nuvexa_close",
        "nuvexa_is_encrypted",
        "nuvexa_insert",
        "nuvexa_insert_many",
        "nuvexa_replace",
        "nuvexa_delete_by_id",
        "nuvexa_find_by_id",
        "nuvexa_execute",
        "nuvexa_ensure_index",
        "nuvexa_list_collections",
        "nuvexa_drop_collection",
        "nuvexa_rename_collection",
        "nuvexa_list_indexes",
        "nuvexa_drop_index",
        "nuvexa_count",
        "nuvexa_checkpoint",
        "nuvexa_backup",
        "nuvexa_compact",
        "nuvexa_restore",
        "nuvexa_stats",
        "nuvexa_change_encryption_key",
        "nuvexa_begin_transaction",
        "nuvexa_commit",
        "nuvexa_rollback",
        "nuvexa_fs_upload",
        "nuvexa_fs_download",
        "nuvexa_fs_metadata",
    ):
        getattr(lib, name).restype = c_int32
    return lib


def _last_error(lib: ctypes.CDLL) -> str:
    out = c_void_p()
    lib.nuvexa_last_error(ctypes.byref(out))
    return _take(lib, out) or "NuvexaDB native call failed."


def _check(lib: ctypes.CDLL, status: int, out: c_void_p | None = None) -> None:
    if status == OK:
        return
    message = _last_error(lib)
    _free(lib, out)
    if status == ENCRYPTION:
        raise NuvexaEncryptionException(message)
    if status == INTEGRITY:
        raise NuvexaIntegrityException(message)
    raise NuvexaException(message)


def _take(lib: ctypes.CDLL, out: c_void_p) -> str:
    if not out.value:
        return ""
    try:
        return ctypes.string_at(out.value).decode("utf-8")
    finally:
        lib.nuvexa_free(out.value)


def _free(lib: ctypes.CDLL, out: c_void_p | None) -> None:
    if out is not None and out.value:
        lib.nuvexa_free(out.value)
        out.value = None
