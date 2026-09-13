# Interop fixtures

Golden NQL cases for every NuvexaDB language binding. There is no committed `.nvx` (those files are gitignored). Each host creates an encrypted file from `cases.json`, inserts the documents, builds the indexes, then runs every `nql` case.

| Field | Meaning |
| --- | --- |
| `key` | Passphrase (`correct-horse`). Open without it must fail. |
| `documents` | JSON objects inserted into `collection` |
| `indexes` | `ensure_index` field lists (`["age"]`, `["city"]`) |
| `cases[].nql` | Same string as `ExecuteAsync` / `nuvexa_execute` |
| `cases[].expectNames` | `name` field of each result document, in order |

C# runs these through the managed engine and the C ABI (`InteropFixtureTests`). Kotlin/Java, Swift, Flutter, React Native, Python, Node, and Go SDKs load the same file. ABI v2 catalog / transaction / GridFS cases are in `ExtendedAbiTests` and each SDK’s extended test.
