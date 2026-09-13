# NuvexaDB encryption

Optional AES-256-GCM at rest. Fail-closed: an encrypted file never opens without the correct key. Missing or wrong key throws `NuvexaEncryptionException` (C ABI status `NUVEXA_ENCRYPTION`). The library does not create, overwrite, or partially open the file.

## When a file is encrypted

`NuvexaCreateOptions.EncryptionKey` (or the C ABI `key` argument) is non-empty at create time. Superblock flag `Encrypted = 1`. `NuvexaDatabase.IsEncrypted` / `nuvexa_is_encrypted` reads the flag without the key.

App `Create()` defaults to 16 MiB Argon2id (mobile-safe). Explorer / CLI `CreateAsync` uses `NuvexaCreateOptions.ForDesktop` (64 MiB). Existing files keep the KDF parameters stored in the superblock.

## Key hierarchy

1. **Passphrase** — UTF-8, supplied by the host. Never stored.
2. **KEK** — Argon2id, 32 bytes. Memory / iterations / parallelism and 16-byte salt are in the superblock.
   - Memory: 8–256 MiB (default 16 MiB)
   - Iterations: 1–16 (default 3)
   - Parallelism: 1–8 (default 2)
3. **DEK** — 32 random bytes. Wraps the page cipher. Wrapped with AES-256-GCM under the KEK; AAD is the 16-byte file id.
4. **Verifier** — plaintext `NVEXA-OK-VERIFY!` encrypted with the KEK (same AAD). Wrong passphrase fails here before pages are touched.

`ChangeEncryptionKey` re-wraps the existing DEK. Pages are not rewritten.

## Pages

Every allocated page except the superblock:

| Bytes | Content |
| --- | --- |
| 0–11 | Random nonce |
| 12–27 | GCM tag (16 bytes) |
| 28–8191 | Ciphertext of the 8164-byte logical page |

AAD is file id (16 bytes) + page id (int64 LE). A tag mismatch is fail-closed (`NuvexaEncryptionException` or integrity failure).

The superblock itself is plaintext plus CRC32 and, when a DEK exists, HMAC-SHA256(DEK, first 400 bytes) at offset 400. All-zero HMAC is legacy and still accepted; the next checkpoint writes the MAC. WAL copies of page 0 use the same HMAC.

## Open path

1. Read page 0. Reject missing `NVX1` magic, bad CRC, or unsupported version / page size.
2. If encrypted and the key is missing or empty → `NuvexaEncryptionException`.
3. Derive KEK, verify the verifier, unwrap the DEK.
4. Verify HMAC when it is not all zeros.
5. Replay WAL. Optionally scan allocated pages (CRC32; AES-GCM when encrypted).

## Bindings

Pass the passphrase into `nuvexa_create` / `nuvexa_open`. Do not implement Argon2 or AES in Java, Kotlin, Swift, Dart, or JavaScript. See [format.md](format.md) and [bindings.md](bindings.md).
