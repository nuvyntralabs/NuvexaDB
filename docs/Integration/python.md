# Python

ctypes wrapper. `NUVEXA_NATIVE_LIB` is **required**.

## 1. Download

From [NuvexaDB v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1), take **both** zips for your machine:

| Your machine | Python zip | Native zip | Library file |
| --- | --- | --- | --- |
| macOS Apple Silicon | `NuvexaDB-Python-osx-arm64.zip` | `NuvexaDB-Native-osx-arm64.zip` | `libnuvexa.dylib` |
| Linux x64 | `NuvexaDB-Python-linux-x64.zip` | `NuvexaDB-Native-linux-x64.zip` | `libnuvexa.so` |
| Windows x64 | `NuvexaDB-Python-win-x64.zip` | `NuvexaDB-Native-win-x64.zip` | `nuvexa.dll` |

Inside the Python zip: `nuvexadb-1.0.1-py3-none-any.whl` (the wheel is tagged `py3-none-any`; the RID on the zip only tells you which CI job built it).

## 2. Empty project

```bash
python3 -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate
mkdir acme_store && cd acme_store
```

## 3. Add the downloaded package

```bash
python3 -m pip install /path/to/nuvexadb-1.0.1-py3-none-any.whl
export NUVEXA_NATIVE_LIB="/absolute/path/to/libnuvexa.dylib"
```

Windows (PowerShell):

```powershell
$env:NUVEXA_NATIVE_LIB = "C:\path\to\nuvexa.dll"
```

Without that variable, `create` / `open` raise `NuvexaException`.

## 4. Create, open, close

```python
from nuvexadb import NuvexaDatabase, NuvexaEncryptionException

path = "app.nvx"
with NuvexaDatabase.create(path, key) as db:  # key=None for plaintext
    pass

if NuvexaDatabase.is_encrypted(path) and not key:
    raise SystemExit("Encrypted .nvx — pass the key")

with NuvexaDatabase.open(path, key) as db:
    pass
```

Opening encrypted without a key raises `NuvexaEncryptionException`.

## 5. Delete the database

```python
from pathlib import Path

db.close()
Path(path).unlink(missing_ok=True)
Path(path + "-wal").unlink(missing_ok=True)
```

## 6. Collections

```python
db.insert("users", '{"name":"Ada","age":36}')
print(db.list_collections())
db.rename_collection("users", "people")
db.drop_collection("people")
```

## 7. Documents

```python
import json

doc_id = db.insert("users", json.dumps({"name": "Ada", "age": 36}))
db.insert_many("users", json.dumps([{"name": "Grace", "age": 85}]))
ada = db.find_by_id("users", doc_id)
db.replace("users", json.dumps({"_id": doc_id, "name": "Ada Lovelace", "age": 36}))
db.delete_by_id("users", doc_id)
rows = db.execute("db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(20)")
```

## 8. Password for an encrypted file

Load the key from the environment or a secret store (`keyring`), not from a committed `.env` in git. A demo may use a throwaway string; production must not use `1234`.

```python
db.change_encryption_key(current_key, next_key)
```

See [Encryption notes](README.md#encrypted-databases--choose-a-password).
