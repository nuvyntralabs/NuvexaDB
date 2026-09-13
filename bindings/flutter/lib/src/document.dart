import 'dart:convert';

class NuvexaDocument {
  NuvexaDocument(this.json) : object = jsonDecode(json) as Map<String, dynamic>;

  final String json;
  final Map<String, dynamic> object;

  String get id => object['_id']?.toString() ?? '';

  String? field(String name) {
    final value = object[name];
    return value == null ? null : value.toString();
  }

  @override
  String toString() => json;

  static NuvexaDocument parse(String json) => NuvexaDocument(json);
}
