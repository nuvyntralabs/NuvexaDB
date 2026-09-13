# Python library

ctypes SDK (`nuvexadb`) over `nuvexa.h`. ABI v2. Python 3.10+.

## Integration

Do not `pip publish` from this clone. Use `pip install -e` or CI `NuvexaDB-Python-<rid>` plus `NuvexaDB-Native-<rid>`.

### Package reference

`NUVEXA_NATIVE_LIB` is **required**.

```bash
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_LIB="$(pwd)/artifacts/native/osx-arm64/libnuvexa.dylib"
python3 -m pip install -e bindings/python
```

```python
from nuvexadb import NuvexaDatabase
```

### Create the database and a collection

First `insert` / `insert_many` into `"users"` creates that collection.

```python
with NuvexaDatabase.create("app.nvx", "sample-key") as db:
    ada_id = db.insert("users", '{"name":"Ada","age":36,"status":"active","address":{"city":"London"}}')
    print(db.list_collections())
```

### CRUD

| Step | API |
| --- | --- |
| Create | `insert` / `insert_many` |
| Read | `find_by_id` |
| Update | `replace` (include `_id`) |
| Delete | `delete_by_id` |

### Complex queries

```python
db.ensure_index("users", "age")
db.ensure_index("users", ["address.city", "status"])
db.execute("db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)")
db.execute('db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })')
db.execute('db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })')
```

Opening without a key raises `NuvexaEncryptionException`.

## In-repo sample

[examples/sample.py](examples/sample.py)

```bash
cd bindings/python
python3 -m unittest tests.test_interop
python3 examples/sample.py
```

See [docs/bindings.md](../../docs/bindings.md).
