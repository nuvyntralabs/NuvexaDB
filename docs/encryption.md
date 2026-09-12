# Encryption

- Algorithm: AES-256-GCM per page. AAD = `fileId | pageId`.
- Key: Argon2id derives a KEK from the passphrase. A random DEK is wrapped with the KEK (same idea as FileVault passphrase wrap). Changing the passphrase re-wraps the DEK; pages are not rewritten.
- Superblock is plaintext so `IsEncrypted` and the IDE unlock dialog can run before the DEK is available.
- Library: missing or wrong key → `NuvexaEncryptionException`. The file is not created, overwritten, or partially opened.
- Explorer / VS / VS Code: catch that exception and prompt (three attempts, then cancel).
- Do not persist the key unless the host opts into platform secure storage (`Plugin.Maui.SecureStoragePlus`).
