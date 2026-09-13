# Python library

ctypes SDK over `nuvexa.h`. ABI v2.

| Piece | Path |
| --- | --- |
| Library | `nuvexadb/` (`pyproject.toml`) |
| Tests | `tests/test_interop.py` |
| Sample | [examples/sample.py](examples/sample.py) |
| This file | `README.md` |

```bash
# from the NuvexaDB repo root
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_LIB="$(pwd)/artifacts/native/osx-arm64/libnuvexa.dylib"
cd bindings/python
python3 -m pip install -e .
python3 -m unittest tests.test_interop
python3 examples/sample.py
```

```python
from nuvexadb import NuvexaDatabase

with NuvexaDatabase.create("app.nvx", "correct-horse") as db:
    db.insert("users", '{"name":"Ada","age":36}')
    db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)")
```

Do not `pip publish` from this clone.
