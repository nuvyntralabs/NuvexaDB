class NuvexaException implements Exception {
  NuvexaException(this.message);
  final String message;

  @override
  String toString() => 'NuvexaException: $message';
}

class NuvexaEncryptionException extends NuvexaException {
  NuvexaEncryptionException(super.message);

  @override
  String toString() => 'NuvexaEncryptionException: $message';
}

class NuvexaIntegrityException extends NuvexaException {
  NuvexaIntegrityException(super.message);

  @override
  String toString() => 'NuvexaIntegrityException: $message';
}
