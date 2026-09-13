import 'dart:io';

import 'package:nuvexadb/nuvexadb.dart';

void main(List<String> args) {
  final path = args.isNotEmpty ? args.first : 'sample.nvx';
  File(path).deleteSync();
  const key = 'sample-key';

  final db = NuvexaDatabase.create(path, key: key);
  try {
    db.insert('users', '{"name":"Ada","age":36}');
    db.ensureIndex('users', ['age']);
    for (final row in db.execute('db.users.find({ age: { \$gte: 21 } }).limit(20)')) {
      print(row);
    }
  } finally {
    db.close();
  }

  print('IsEncrypted: ${NuvexaDatabase.isEncrypted(path)}');
  try {
    NuvexaDatabase.open(path);
    print('ERROR: open without key should have failed.');
  } on NuvexaEncryptionException catch (ex) {
    print('Lib fail-closed: ${ex.message}');
  }
}
