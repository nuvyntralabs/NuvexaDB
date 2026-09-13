import 'dart:convert';
import 'dart:ffi';

import 'package:ffi/ffi.dart';

import 'document.dart';
import 'exceptions.dart';
import 'library.dart';

class NuvexaDatabase {
  NuvexaDatabase._(this._lib, this._handle);

  final NuvexaLibrary _lib;
  int _handle;

  static NuvexaDatabase create(String path, {String? key, NuvexaLibrary? library}) =>
      _openOrCreate(path, key, create: true, library: library);

  static NuvexaDatabase open(String path, {String? key, NuvexaLibrary? library}) =>
      _openOrCreate(path, key, create: false, library: library);

  static bool isEncrypted(String path, {NuvexaLibrary? library}) {
    final lib = library ?? NuvexaLibrary.load();
    return using((arena) {
      final encrypted = arena<Int32>();
      final status = lib.isEncrypted(path.toNativeUtf8(allocator: arena), encrypted);
      _throwIf(lib, status);
      return encrypted.value != 0;
    });
  }

  static NuvexaDatabase _openOrCreate(
    String path,
    String? key, {
    required bool create,
    NuvexaLibrary? library,
  }) {
    final lib = library ?? NuvexaLibrary.load();
    return using((arena) {
      final handle = arena<IntPtr>();
      final Pointer<Utf8> keyPtr =
          key == null ? nullptr : key.toNativeUtf8(allocator: arena);
      final status = create
          ? lib.create(path.toNativeUtf8(allocator: arena), keyPtr, handle)
          : lib.open(path.toNativeUtf8(allocator: arena), keyPtr, handle);
      _throwIf(lib, status);
      return NuvexaDatabase._(lib, handle.value);
    });
  }

  String insert(String collection, String json) {
    return using((arena) {
      final idOut = arena<Pointer<Utf8>>();
      final status = _lib.insert(
        _handle,
        collection.toNativeUtf8(allocator: arena),
        json.toNativeUtf8(allocator: arena),
        idOut,
      );
      _throwIf(_lib, status, idOut);
      return _take(idOut);
    });
  }

  void replace(String collection, String json) {
    using((arena) {
      final status = _lib.replace(
        _handle,
        collection.toNativeUtf8(allocator: arena),
        json.toNativeUtf8(allocator: arena),
      );
      _throwIf(_lib, status);
    });
  }

  bool deleteById(String collection, String id) {
    return using((arena) {
      final deleted = arena<Int32>();
      final status = _lib.deleteById(
        _handle,
        collection.toNativeUtf8(allocator: arena),
        id.toNativeUtf8(allocator: arena),
        deleted,
      );
      _throwIf(_lib, status);
      return deleted.value != 0;
    });
  }

  NuvexaDocument? findById(String collection, String id) {
    return using((arena) {
      final jsonOut = arena<Pointer<Utf8>>();
      final status = _lib.findById(
        _handle,
        collection.toNativeUtf8(allocator: arena),
        id.toNativeUtf8(allocator: arena),
        jsonOut,
      );
      if (status == notFound) {
        _free(jsonOut);
        return null;
      }
      _throwIf(_lib, status, jsonOut);
      return NuvexaDocument.parse(_take(jsonOut));
    });
  }

  List<NuvexaDocument> execute(String nql) {
    return using((arena) {
      final jsonOut = arena<Pointer<Utf8>>();
      final status = _lib.execute(_handle, nql.toNativeUtf8(allocator: arena), jsonOut);
      _throwIf(_lib, status, jsonOut);
      final decoded = jsonDecode(_take(jsonOut)) as List<dynamic>;
      return decoded
          .map((row) => NuvexaDocument(jsonEncode(row)))
          .toList();
    });
  }

  void ensureIndex(String collection, List<String> fields) {
    final payload = jsonEncode(fields.length == 1 ? fields.first : fields);
    using((arena) {
      final status = _lib.ensureIndex(
        _handle,
        collection.toNativeUtf8(allocator: arena),
        payload.toNativeUtf8(allocator: arena),
      );
      _throwIf(_lib, status);
    });
  }

  static int abiVersion({NuvexaLibrary? library}) =>
      (library ?? NuvexaLibrary.load()).abiVersionFn();

  static void restore(String backupPath, String destPath,
      {bool overwrite = false, NuvexaLibrary? library}) {
    final lib = library ?? NuvexaLibrary.load();
    using((arena) {
      final status = lib.restore(
        backupPath.toNativeUtf8(allocator: arena),
        destPath.toNativeUtf8(allocator: arena),
        overwrite ? 1 : 0,
      );
      _throwIf(lib, status);
    });
  }

  List<String> insertMany(String collection, String jsonArray) {
    return using((arena) {
      final idsOut = arena<Pointer<Utf8>>();
      final status = _lib.insertMany(
        _handle,
        collection.toNativeUtf8(allocator: arena),
        jsonArray.toNativeUtf8(allocator: arena),
        idsOut,
      );
      _throwIf(_lib, status, idsOut);
      return (jsonDecode(_take(idsOut)) as List<dynamic>).cast<String>();
    });
  }

  List<String> listCollections() {
    return using((arena) {
      final jsonOut = arena<Pointer<Utf8>>();
      final status = _lib.listCollections(_handle, jsonOut);
      _throwIf(_lib, status, jsonOut);
      return (jsonDecode(_take(jsonOut)) as List<dynamic>).cast<String>();
    });
  }

  void dropCollection(String collection) {
    using((arena) {
      _throwIf(_lib, _lib.dropCollection(_handle, collection.toNativeUtf8(allocator: arena)));
    });
  }

  void renameCollection(String from, String to) {
    using((arena) {
      _throwIf(
        _lib,
        _lib.renameCollection(
          _handle,
          from.toNativeUtf8(allocator: arena),
          to.toNativeUtf8(allocator: arena),
        ),
      );
    });
  }

  List<Map<String, dynamic>> listIndexes(String collection) {
    return using((arena) {
      final jsonOut = arena<Pointer<Utf8>>();
      final status = _lib.listIndexes(
        _handle,
        collection.toNativeUtf8(allocator: arena),
        jsonOut,
      );
      _throwIf(_lib, status, jsonOut);
      return (jsonDecode(_take(jsonOut)) as List<dynamic>)
          .cast<Map<String, dynamic>>();
    });
  }

  void dropIndex(String collection, String name) {
    using((arena) {
      _throwIf(
        _lib,
        _lib.dropIndex(
          _handle,
          collection.toNativeUtf8(allocator: arena),
          name.toNativeUtf8(allocator: arena),
        ),
      );
    });
  }

  int count(String collection, [String filterJson = '{}']) {
    return using((arena) {
      final n = arena<Int64>();
      final status = _lib.count(
        _handle,
        collection.toNativeUtf8(allocator: arena),
        filterJson.toNativeUtf8(allocator: arena),
        n,
      );
      _throwIf(_lib, status);
      return n.value;
    });
  }

  void checkpoint() => _throwIf(_lib, _lib.checkpoint(_handle));

  void backup(String destPath) {
    using((arena) {
      _throwIf(_lib, _lib.backup(_handle, destPath.toNativeUtf8(allocator: arena)));
    });
  }

  void compact() => _throwIf(_lib, _lib.compact(_handle));

  Map<String, dynamic> stats() {
    return using((arena) {
      final jsonOut = arena<Pointer<Utf8>>();
      final status = _lib.stats(_handle, jsonOut);
      _throwIf(_lib, status, jsonOut);
      return jsonDecode(_take(jsonOut)) as Map<String, dynamic>;
    });
  }

  void changeEncryptionKey(String currentKey, String nextKey) {
    using((arena) {
      _throwIf(
        _lib,
        _lib.changeEncryptionKey(
          _handle,
          currentKey.toNativeUtf8(allocator: arena),
          nextKey.toNativeUtf8(allocator: arena),
        ),
      );
    });
  }

  void beginTransaction() => _throwIf(_lib, _lib.beginTransaction(_handle));

  void commit() => _throwIf(_lib, _lib.commit(_handle));

  void rollback() => _throwIf(_lib, _lib.rollback(_handle));

  String uploadFile(String fileName, String sourcePath, {int chunkSize = 0}) {
    return using((arena) {
      final idOut = arena<Pointer<Utf8>>();
      final status = _lib.fsUpload(
        _handle,
        fileName.toNativeUtf8(allocator: arena),
        sourcePath.toNativeUtf8(allocator: arena),
        chunkSize,
        idOut,
      );
      _throwIf(_lib, status, idOut);
      return _take(idOut);
    });
  }

  bool downloadFile(String fileId, String destPath) {
    return using((arena) {
      final found = arena<Int32>();
      final status = _lib.fsDownload(
        _handle,
        fileId.toNativeUtf8(allocator: arena),
        destPath.toNativeUtf8(allocator: arena),
        found,
      );
      _throwIf(_lib, status);
      return found.value != 0;
    });
  }

  NuvexaDocument? fileMetadata(String fileId) {
    return using((arena) {
      final jsonOut = arena<Pointer<Utf8>>();
      final status = _lib.fsMetadata(
        _handle,
        fileId.toNativeUtf8(allocator: arena),
        jsonOut,
      );
      if (status == notFound) {
        _free(jsonOut);
        return null;
      }
      _throwIf(_lib, status, jsonOut);
      return NuvexaDocument.parse(_take(jsonOut));
    });
  }

  void close() {
    if (_handle == 0) {
      return;
    }
    final status = _lib.close(_handle);
    _handle = 0;
    _throwIf(_lib, status);
  }

  String _take(Pointer<Pointer<Utf8>> out) {
    final pointer = out.value;
    if (pointer == nullptr) {
      return '';
    }
    try {
      return pointer.toDartString();
    } finally {
      _lib.free(pointer);
    }
  }

  void _free(Pointer<Pointer<Utf8>> out) {
    if (out.value != nullptr) {
      _lib.free(out.value);
    }
  }

  static void _throwIf(NuvexaLibrary lib, int status, [Pointer<Pointer<Utf8>>? out]) {
    if (status == ok) {
      return;
    }
    final message = _readError(lib);
    if (out != null && out.value != nullptr) {
      lib.free(out.value);
    }
    switch (status) {
      case encryption:
        throw NuvexaEncryptionException(message);
      case integrity:
        throw NuvexaIntegrityException(message);
      default:
        throw NuvexaException(message);
    }
  }

  static String _readError(NuvexaLibrary lib) {
    return using((arena) {
      final message = arena<Pointer<Utf8>>();
      lib.lastError(message);
      final pointer = message.value;
      if (pointer == nullptr) {
        return 'NuvexaDB native call failed.';
      }
      try {
        return pointer.toDartString();
      } finally {
        lib.free(pointer);
      }
    });
  }
}
