export class NuvexaException extends Error {
  constructor(message) {
    super(message);
    this.name = "NuvexaException";
  }
}

export class NuvexaEncryptionException extends NuvexaException {
  constructor(message) {
    super(message);
    this.name = "NuvexaEncryptionException";
  }
}

export class NuvexaIntegrityException extends NuvexaException {
  constructor(message) {
    super(message);
    this.name = "NuvexaIntegrityException";
  }
}

export const Status = {
  OK: 0,
  ERROR: 1,
  ENCRYPTION: 2,
  INTEGRITY: 3,
  NOT_FOUND: 4,
};

export function throwStatus(status, message) {
  const text = message || "NuvexaDB native call failed.";
  if (status === Status.ENCRYPTION) {
    throw new NuvexaEncryptionException(text);
  }
  if (status === Status.INTEGRITY) {
    throw new NuvexaIntegrityException(text);
  }
  throw new NuvexaException(text);
}
