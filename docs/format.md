# NuvexaDB file format (version 2)

A `.nvx` file is one portable embedded database. **Creates and writes always use format 2.** Format **1** is deprecated: the engine still **reads** it (and leftover `n:` index keys / WAL v1 headers). Do not write format 1. Language bindings must not reimplement this format. They call the Native AOT C ABI, which uses the same managed engine as `Nuventra.NuvexaDB`.

## Layout

| Item | Value |
| --- | --- |
| Magic (bytes 0–3) | `NVX1` |
| Format version | `2` on every create / write; `1` deprecated, accepted on open (uint16 LE at offset 4) |
| Physical page size | 8192 bytes |
| Logical payload (after AES-GCM overhead) | 8164 bytes (`8192 - 12 nonce - 16 tag`) |
| Page header | 40 bytes |
| Slot directory entry | 4 bytes (offset + length, uint16 LE each) |
| Catalog page id | `1` |
| First allocatable page id | `2` |
| Companion WAL | `<path>.nvx-wal` (magic `NVXW`) |
| Exclusive lock | `FileShare.None` — one process, one open path |

Page 0 is the superblock. It is never AES-GCM page-encrypted. Encrypted files HMAC-SHA256 the first 400 bytes of page 0 with the DEK (32-byte MAC at offset 400). Superblock CRC32 covers those 400 bytes (CRC stored at offset 198; the CRC field is zeroed while computing).

## Superblock (page 0, little-endian)

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | Magic `NVX1` |
| 4 | 2 | Version |
| 6 | 2 | Flags (`Encrypted = 1`, `CompactNeeded = 2`, `IntegrityProtected = 4`) |
| 8 | 4 | Page size (must be 8192) |
| 12 | 8 | Page count |
| 20 | 16 | File id (AAD / wrap nonce context) |
| 36 | 8 | Catalog page id |
| 44 | 8 | Next page id |
| 52 | 8 | Committed LSN |
| 68 | 4 | Argon2id memory KiB |
| 72 | 4 | Argon2id iterations |
| 76 | 2 | Argon2id parallelism |
| 78 | 16 | KDF salt |
| 94 | 12 | Verifier nonce |
| 106 | 16 | Verifier tag |
| 122 | 16 | Verifier ciphertext (`NVEXA-OK-VERIFY!`) |
| 138 | 12 | DEK wrap nonce |
| 150 | 16 | DEK wrap tag |
| 166 | 32 | Wrapped DEK (AES-256-GCM) |
| 198 | 4 | CRC32 of bytes 0–399 (field zeroed during compute) |
| 400 | 32 | HMAC-SHA256(DEK, bytes 0–399); all-zero means legacy, still accepted |

See [encryption.md](encryption.md) for KDF and page cipher.

## Pages

Each allocated page (except the superblock) is a slotted logical buffer of 8164 bytes:

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 1 | Page type |
| 2 | 2 | Item count |
| 4 | 4 | CRC32 of the logical page (checksum field zeroed during compute) |
| 8 | 8 | Page id |
| 16 | 8 | LSN |
| 24 | 8 | Next page id (overflow / chain) |
| 32 | 8 | Right sibling (B+tree) |

Types: `Free=0`, `Super=1`, `Catalog=2`, `Data=3`, `IndexLeaf=4`, `IndexInternal=5`, `SecondaryIndexCatalog=6`.

Slots grow from the header; the slot directory grows backward from the end of the logical page. Documents larger than a page are chained (`MaxDocumentBytes` = 16 MiB). Collection names are at most 120 bytes.

## Documents

New data-page payloads are **BSON**. Public APIs stay JSON (`NuvexaDocument.Parse` / `ToJson`, NQL, Explorer, C ABI). A payload that looks like `{…}` is treated as legacy UTF-8 JSON. Mixed BSON/JSON files are valid.

## Index keys

Keys are type prefix plus NUL plus document id:

| Prefix | Meaning |
| --- | --- |
| `s:` | string |
| `n:` | v1 number (`G17` invariant culture; **not** numeric-order-preserving) |
| `d:` | v2 number (8 IEEE754 sortable bytes; lex order = numeric order) |
| `b:0` / `b:1` | boolean |
| `z:` | null |
| `j:` | other JSON |

Compound indexes join field paths and value prefixes with U+001F. Equality, string ranges, and **format-2 numeric ranges** can IXSCAN with lo/hi bounds. Deprecated format-1 `n:` keys are still read (walk that prefix and apply the real compare). New numeric index entries are always `d:`.

## WAL (`*.nvx-wal`)

v1 header (22 bytes): `NVXW` + uint16 version + 16 reserved bytes.  
v2 header (32 bytes): `NVXW` + uint16 version + int32 page size + 22 reserved bytes. Records:

- Page: type `1`, page id, LSN, length (8192), physical page, CRC32 of the page
- Commit: type `2`, LSN, CRC32 of type+LSN

Replay applies page records up to the last valid commit. Encrypted checkpoints write the same DEK HMAC on WAL copies of page 0. Legacy all-zero MAC records still replay. Truncate after a successful checkpoint.

## Integrity

Open checks superblock CRC (and HMAC when encrypted and non-zero). `NuvexaOpenOptions.VerifyIntegrity` (default true) scans allocated pages when the file is ≤ `IntegrityScanMaxBytes` (default 64 MiB). A mismatch throws `NuvexaIntegrityException`. The engine does not repair a failed file.

## Bindings

Language SDKs (Java, Kotlin, Swift, Flutter, React Native, Python, Node, Go, C++) must treat `.nvx` as an opaque file and call [bindings](bindings.md). Do not parse pages from those SDKs. Sharing model: [architecture.md](architecture.md).
