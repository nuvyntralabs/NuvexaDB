import 'dart:convert';
import 'dart:io';

import 'package:nuvexadb/nuvexadb.dart';

void main(List<String> args) {
  final path = args.isNotEmpty ? args.first : 'sample.nvx';
  final file = File(path);
  if (file.existsSync()) {
    file.deleteSync();
  }
  const key = 'sample-key';

  final db = NuvexaDatabase.create(path, key: key);
  try {
    final adaId = db.insert(
      'users',
      jsonEncode({
        'name': 'Ada',
        'age': 36,
        'status': 'active',
        'address': {'city': 'London'},
      }),
    );
    db.insertMany(
      'users',
      jsonEncode([
        {'name': 'Grace', 'age': 85, 'status': 'retired', 'address': {'city': 'New York'}},
        {'name': 'Cara', 'age': 21, 'status': 'active', 'address': {'city': 'Bengaluru'}},
        {'name': 'Alan', 'age': 42, 'status': 'active', 'address': {'city': 'London'}},
      ]),
    );
    final scratchId = db.insert(
      'users',
      jsonEncode({
        'name': 'Scratch',
        'age': 19,
        'status': 'active',
        'address': {'city': 'Paris'},
      }),
    );
    db.ensureIndex('users', ['age']);
    db.ensureIndex('users', ['address.city', 'status']);
    print('Created collection users. Collections: ${db.listCollections()}');
    print('Read Ada: ${db.findById('users', adaId)}');
    db.replace(
      'users',
      jsonEncode({
        '_id': adaId,
        'name': 'Ada Lovelace',
        'age': 36,
        'status': 'active',
        'address': {'city': 'London'},
      }),
    );
    print('Updated Ada: ${db.findById('users', adaId)}');
    print('Deleted scratch: ${db.deleteById('users', scratchId)}');
    print('-- NQL age >= 21, sort name, limit 10 --');
    for (final row in db.execute('db.users.find({ age: { \$gte: 21 } }).sort({ name: 1 }).limit(10)')) {
      print(row);
    }
    print('-- NQL \$and London + active --');
    for (final row in db.execute(
      'db.users.find({ \$and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })',
    )) {
      print(row);
    }
    print('-- NQL \$or age < 30 or New York --');
    for (final row in db.execute(
      'db.users.find({ \$or: [ { age: { \$lt: 30 } }, { "address.city": "New York" } ] })',
    )) {
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
