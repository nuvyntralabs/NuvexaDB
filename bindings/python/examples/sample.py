import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from nuvexadb import NuvexaDatabase, NuvexaEncryptionException


def main() -> None:
    path = sys.argv[1] if len(sys.argv) > 1 else "sample.nvx"
    Path(path).unlink(missing_ok=True)
    with NuvexaDatabase.create(path, "sample-key") as db:
        db.insert("users", json.dumps({"name": "Ada", "age": 36}))
        db.ensure_index("users", "age")
        for row in db.execute("db.users.find({ age: { $gte: 21 } }).limit(20)"):
            print(row.json)
        print("collections", db.list_collections())
    print("encrypted", NuvexaDatabase.is_encrypted(path))
    try:
        NuvexaDatabase.open(path)
        raise SystemExit("open without key should fail")
    except NuvexaEncryptionException as ex:
        print("fail-closed", ex)


if __name__ == "__main__":
    main()
