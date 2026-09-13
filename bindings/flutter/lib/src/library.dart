import 'dart:ffi';
import 'dart:io';

import 'package:ffi/ffi.dart';

const ok = 0;
const error = 1;
const encryption = 2;
const integrity = 3;
const notFound = 4;
const abiVersion = 2;

typedef _VersionNative = Int32 Function();
typedef _VersionDart = int Function();

typedef _CreateNative = Int32 Function(
    Pointer<Utf8> path, Pointer<Utf8> key, Pointer<IntPtr> handle);
typedef _CreateDart = int Function(
    Pointer<Utf8> path, Pointer<Utf8> key, Pointer<IntPtr> handle);

typedef _CloseNative = Int32 Function(IntPtr handle);
typedef _CloseDart = int Function(int handle);

typedef _IsEncryptedNative = Int32 Function(
    Pointer<Utf8> path, Pointer<Int32> encrypted);
typedef _IsEncryptedDart = int Function(
    Pointer<Utf8> path, Pointer<Int32> encrypted);

typedef _InsertNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> collection, Pointer<Utf8> json, Pointer<Pointer<Utf8>> idOut);
typedef _InsertDart = int Function(
    int handle, Pointer<Utf8> collection, Pointer<Utf8> json, Pointer<Pointer<Utf8>> idOut);

typedef _ReplaceNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> collection, Pointer<Utf8> json);
typedef _ReplaceDart = int Function(
    int handle, Pointer<Utf8> collection, Pointer<Utf8> json);

typedef _DeleteNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> collection, Pointer<Utf8> id, Pointer<Int32> deleted);
typedef _DeleteDart = int Function(
    int handle, Pointer<Utf8> collection, Pointer<Utf8> id, Pointer<Int32> deleted);

typedef _FindNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> collection, Pointer<Utf8> id, Pointer<Pointer<Utf8>> jsonOut);
typedef _FindDart = int Function(
    int handle, Pointer<Utf8> collection, Pointer<Utf8> id, Pointer<Pointer<Utf8>> jsonOut);

typedef _ExecuteNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> nql, Pointer<Pointer<Utf8>> jsonOut);
typedef _ExecuteDart = int Function(
    int handle, Pointer<Utf8> nql, Pointer<Pointer<Utf8>> jsonOut);

typedef _EnsureIndexNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> collection, Pointer<Utf8> fields);
typedef _EnsureIndexDart = int Function(
    int handle, Pointer<Utf8> collection, Pointer<Utf8> fields);

typedef _JsonOutNative = Int32 Function(IntPtr handle, Pointer<Pointer<Utf8>> jsonOut);
typedef _JsonOutDart = int Function(int handle, Pointer<Pointer<Utf8>> jsonOut);

typedef _NameNative = Int32 Function(IntPtr handle, Pointer<Utf8> name);
typedef _NameDart = int Function(int handle, Pointer<Utf8> name);

typedef _RenameNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> from, Pointer<Utf8> to);
typedef _RenameDart = int Function(int handle, Pointer<Utf8> from, Pointer<Utf8> to);

typedef _ListIndexesNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> collection, Pointer<Pointer<Utf8>> jsonOut);
typedef _ListIndexesDart = int Function(
    int handle, Pointer<Utf8> collection, Pointer<Pointer<Utf8>> jsonOut);

typedef _DropIndexNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> collection, Pointer<Utf8> name);
typedef _DropIndexDart = int Function(
    int handle, Pointer<Utf8> collection, Pointer<Utf8> name);

typedef _CountNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> collection, Pointer<Utf8> filter, Pointer<Int64> count);
typedef _CountDart = int Function(
    int handle, Pointer<Utf8> collection, Pointer<Utf8> filter, Pointer<Int64> count);

typedef _PathNative = Int32 Function(IntPtr handle, Pointer<Utf8> path);
typedef _PathDart = int Function(int handle, Pointer<Utf8> path);

typedef _RestoreNative = Int32 Function(
    Pointer<Utf8> backup, Pointer<Utf8> dest, Int32 overwrite);
typedef _RestoreDart = int Function(
    Pointer<Utf8> backup, Pointer<Utf8> dest, int overwrite);

typedef _RekeyNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> currentKey, Pointer<Utf8> nextKey);
typedef _RekeyDart = int Function(
    int handle, Pointer<Utf8> currentKey, Pointer<Utf8> nextKey);

typedef _UploadNative = Int32 Function(IntPtr handle, Pointer<Utf8> fileName,
    Pointer<Utf8> sourcePath, Int32 chunkSize, Pointer<Pointer<Utf8>> idOut);
typedef _UploadDart = int Function(int handle, Pointer<Utf8> fileName,
    Pointer<Utf8> sourcePath, int chunkSize, Pointer<Pointer<Utf8>> idOut);

typedef _DownloadNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> fileId, Pointer<Utf8> dest, Pointer<Int32> found);
typedef _DownloadDart = int Function(
    int handle, Pointer<Utf8> fileId, Pointer<Utf8> dest, Pointer<Int32> found);

typedef _MetadataNative = Int32 Function(
    IntPtr handle, Pointer<Utf8> fileId, Pointer<Pointer<Utf8>> jsonOut);
typedef _MetadataDart = int Function(
    int handle, Pointer<Utf8> fileId, Pointer<Pointer<Utf8>> jsonOut);

typedef _LastErrorNative = Int32 Function(Pointer<Pointer<Utf8>> message);
typedef _LastErrorDart = int Function(Pointer<Pointer<Utf8>> message);

typedef _FreeNative = Void Function(Pointer<Utf8> pointer);
typedef _FreeDart = void Function(Pointer<Utf8> pointer);

class NuvexaLibrary {
  NuvexaLibrary._(this._lib)
      : abiVersionFn = _lib.lookupFunction<_VersionNative, _VersionDart>('nuvexa_abi_version'),
        create = _lib.lookupFunction<_CreateNative, _CreateDart>('nuvexa_create'),
        open = _lib.lookupFunction<_CreateNative, _CreateDart>('nuvexa_open'),
        close = _lib.lookupFunction<_CloseNative, _CloseDart>('nuvexa_close'),
        isEncrypted =
            _lib.lookupFunction<_IsEncryptedNative, _IsEncryptedDart>('nuvexa_is_encrypted'),
        insert = _lib.lookupFunction<_InsertNative, _InsertDart>('nuvexa_insert'),
        insertMany = _lib.lookupFunction<_InsertNative, _InsertDart>('nuvexa_insert_many'),
        replace = _lib.lookupFunction<_ReplaceNative, _ReplaceDart>('nuvexa_replace'),
        deleteById = _lib.lookupFunction<_DeleteNative, _DeleteDart>('nuvexa_delete_by_id'),
        findById = _lib.lookupFunction<_FindNative, _FindDart>('nuvexa_find_by_id'),
        execute = _lib.lookupFunction<_ExecuteNative, _ExecuteDart>('nuvexa_execute'),
        ensureIndex =
            _lib.lookupFunction<_EnsureIndexNative, _EnsureIndexDart>('nuvexa_ensure_index'),
        listCollections =
            _lib.lookupFunction<_JsonOutNative, _JsonOutDart>('nuvexa_list_collections'),
        dropCollection = _lib.lookupFunction<_NameNative, _NameDart>('nuvexa_drop_collection'),
        renameCollection =
            _lib.lookupFunction<_RenameNative, _RenameDart>('nuvexa_rename_collection'),
        listIndexes =
            _lib.lookupFunction<_ListIndexesNative, _ListIndexesDart>('nuvexa_list_indexes'),
        dropIndex = _lib.lookupFunction<_DropIndexNative, _DropIndexDart>('nuvexa_drop_index'),
        count = _lib.lookupFunction<_CountNative, _CountDart>('nuvexa_count'),
        checkpoint = _lib.lookupFunction<_CloseNative, _CloseDart>('nuvexa_checkpoint'),
        backup = _lib.lookupFunction<_PathNative, _PathDart>('nuvexa_backup'),
        compact = _lib.lookupFunction<_CloseNative, _CloseDart>('nuvexa_compact'),
        restore = _lib.lookupFunction<_RestoreNative, _RestoreDart>('nuvexa_restore'),
        stats = _lib.lookupFunction<_JsonOutNative, _JsonOutDart>('nuvexa_stats'),
        changeEncryptionKey =
            _lib.lookupFunction<_RekeyNative, _RekeyDart>('nuvexa_change_encryption_key'),
        beginTransaction =
            _lib.lookupFunction<_CloseNative, _CloseDart>('nuvexa_begin_transaction'),
        commit = _lib.lookupFunction<_CloseNative, _CloseDart>('nuvexa_commit'),
        rollback = _lib.lookupFunction<_CloseNative, _CloseDart>('nuvexa_rollback'),
        fsUpload = _lib.lookupFunction<_UploadNative, _UploadDart>('nuvexa_fs_upload'),
        fsDownload = _lib.lookupFunction<_DownloadNative, _DownloadDart>('nuvexa_fs_download'),
        fsMetadata = _lib.lookupFunction<_MetadataNative, _MetadataDart>('nuvexa_fs_metadata'),
        lastError = _lib.lookupFunction<_LastErrorNative, _LastErrorDart>('nuvexa_last_error'),
        free = _lib.lookupFunction<_FreeNative, _FreeDart>('nuvexa_free');

  final DynamicLibrary _lib;
  final _VersionDart abiVersionFn;
  final _CreateDart create;
  final _CreateDart open;
  final _CloseDart close;
  final _IsEncryptedDart isEncrypted;
  final _InsertDart insert;
  final _InsertDart insertMany;
  final _ReplaceDart replace;
  final _DeleteDart deleteById;
  final _FindDart findById;
  final _ExecuteDart execute;
  final _EnsureIndexDart ensureIndex;
  final _JsonOutDart listCollections;
  final _NameDart dropCollection;
  final _RenameDart renameCollection;
  final _ListIndexesDart listIndexes;
  final _DropIndexDart dropIndex;
  final _CountDart count;
  final _CloseDart checkpoint;
  final _PathDart backup;
  final _CloseDart compact;
  final _RestoreDart restore;
  final _JsonOutDart stats;
  final _RekeyDart changeEncryptionKey;
  final _CloseDart beginTransaction;
  final _CloseDart commit;
  final _CloseDart rollback;
  final _UploadDart fsUpload;
  final _DownloadDart fsDownload;
  final _MetadataDart fsMetadata;
  final _LastErrorDart lastError;
  final _FreeDart free;

  static NuvexaLibrary? _instance;

  static NuvexaLibrary load([String? libraryPath]) {
    return _instance ??= NuvexaLibrary._(_open(libraryPath));
  }

  static DynamicLibrary _open(String? libraryPath) {
    final path = libraryPath ?? Platform.environment['NUVEXA_NATIVE_LIB'];
    if (path != null && path.isNotEmpty) {
      return DynamicLibrary.open(path);
    }
    if (Platform.isIOS) {
      return DynamicLibrary.process();
    }
    if (Platform.isMacOS) {
      try {
        return DynamicLibrary.open('libnuvexa.dylib');
      } on ArgumentError {
        return DynamicLibrary.open('nuvexa.dylib');
      }
    }
    if (Platform.isWindows) {
      return DynamicLibrary.open('nuvexa.dll');
    }
    return DynamicLibrary.open('libnuvexa.so');
  }
}
