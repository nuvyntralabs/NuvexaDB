# .nvx file format (NVX1)

Physical page size is 8192 bytes.

## Superblock (page 0, plaintext)

| Offset | Field |
| --- | --- |
| 0-3 | Magic `NVX1` |
| 4-5 | Format version (1) |
| 6-7 | Flags (`Encrypted`, `CompactNeeded`) |
| 8-11 | Page size |
| 12-19 | Page count |
| 20-35 | File id (AAD) |
| 36-43 | Catalog page id |
| 44-51 | Next page id |
| 52-59 | Committed LSN |
| 68-75 | Argon2id memory KiB + iterations |
| 76-77 | Argon2id parallelism |
| 78-93 | Salt |
| 94-197 | Verifier + wrapped DEK (AES-256-GCM) |

## Data pages

Logical payload is 8164 bytes (28-byte cipher overhead reserved on disk when encrypted). Slotted documents. Overflow documents chain extra pages.

## Indexes

B+tree on `_id` per collection. Secondary indexes are explicit (`EnsureIndex`).

## WAL

Sibling file `*.nvx-wal`. Page records + commit records. Open replays committed records. Checkpoint writes pages into the main file and truncates the WAL.
