import 'dart:convert';
import 'dart:io';

import 'package:nuvexadb/nuvexadb.dart';
import 'package:test/test.dart';

void main() {
  final libPath = Platform.environment['NUVEXA_NATIVE_LIB'];
  if (libPath == null || libPath.isEmpty) {
    return;
  }

  test('golden NQL cases', () {
    final fixture = jsonDecode(File(_findCases()).readAsStringSync()) as Map<String, dynamic>;
    final key = fixture['key'] as String;
    final collection = fixture['collection'] as String;
    final file = File('${Directory.systemTemp.path}/nuvexa-flutter-${DateTime.now().microsecondsSinceEpoch}.nvx');
    if (file.existsSync()) {
      file.deleteSync();
    }

    final created = NuvexaDatabase.create(file.path, key: key);
    try {
      _seed(created, fixture, collection);
      _assertCases(created, fixture);
    } finally {
      created.close();
    }

    expect(NuvexaDatabase.isEncrypted(file.path), isTrue);
    expect(() => NuvexaDatabase.open(file.path), throwsA(isA<NuvexaEncryptionException>()));

    final opened = NuvexaDatabase.open(file.path, key: key);
    try {
      _assertCases(opened, fixture);
    } finally {
      opened.close();
    }
  });

  test('extended catalog and transaction', () {
    expect(NuvexaDatabase.abiVersion(), 2);
    final file = File('${Directory.systemTemp.path}/nuvexa-flutter-ext-${DateTime.now().microsecondsSinceEpoch}.nvx');
    if (file.existsSync()) {
      file.deleteSync();
    }
    final db = NuvexaDatabase.create(file.path, key: 'key');
    try {
      db.insert('users', '{"name":"Ada","age":36}');
      db.insertMany('users', '[{"name":"Ben","age":12}]');
      expect(db.listCollections(), ['users']);
      expect(db.count('users'), 2);
      db.beginTransaction();
      db.insert('users', '{"name":"Zoe","age":40}');
      db.rollback();
      expect(db.count('users'), 2);
    } finally {
      db.close();
    }
  });
}

void _seed(NuvexaDatabase db, Map<String, dynamic> fixture, String collection) {
  for (final doc in fixture['documents'] as List<dynamic>) {
    db.insert(collection, jsonEncode(doc));
  }
  for (final fields in fixture['indexes'] as List<dynamic>) {
    db.ensureIndex(collection, (fields as List<dynamic>).cast<String>());
  }
}

void _assertCases(NuvexaDatabase db, Map<String, dynamic> fixture) {
  for (final query in fixture['cases'] as List<dynamic>) {
    final row = query as Map<String, dynamic>;
    final names = db.execute(row['nql'] as String).map((doc) => doc.field('name')).toList();
    expect(names, row['expectNames'], reason: row['name'] as String);
  }
}

String _findCases() {
  var dir = Directory.current;
  for (var i = 0; i < 8; i++) {
    final candidate = File('${dir.path}/tests/interop/cases.json');
    if (candidate.existsSync()) {
      return candidate.path;
    }
    dir = dir.parent;
  }
  throw StateError('tests/interop/cases.json was not found.');
}
