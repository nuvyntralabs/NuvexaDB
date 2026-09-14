import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from nuvexadb import NuvexaDatabase, NuvexaEncryptionException


def main() -> None:
    path = sys.argv[1] if len(sys.argv) > 1 else "sample.nvx"
    Path(path).unlink(missing_ok=True)
    # create writes format 2
    with NuvexaDatabase.create(path, "sample-key") as db:
        ada_id = db.insert(
            "users",
            json.dumps({"name": "Ada", "age": 36, "status": "active", "address": {"city": "London"}}),
        )
        db.insert_many(
            "users",
            json.dumps(
                [
                    {"name": "Grace", "age": 85, "status": "retired", "address": {"city": "New York"}},
                    {"name": "Cara", "age": 21, "status": "active", "address": {"city": "Bengaluru"}},
                    {"name": "Alan", "age": 42, "status": "active", "address": {"city": "London"}},
                ]
            ),
        )
        scratch_id = db.insert(
            "users",
            json.dumps({"name": "Scratch", "age": 19, "status": "active", "address": {"city": "Paris"}}),
        )
        db.ensure_index("users", "age")
        db.ensure_index("users", ["address.city", "status"])
        print("Created collection users. Collections:", db.list_collections())
        print("Read Ada:", db.find_by_id("users", ada_id).json)
        db.replace(
            "users",
            json.dumps(
                {
                    "_id": ada_id,
                    "name": "Ada Lovelace",
                    "age": 36,
                    "status": "active",
                    "address": {"city": "London"},
                }
            ),
        )
        print("Updated Ada:", db.find_by_id("users", ada_id).json)
        print("Deleted scratch:", db.delete_by_id("users", scratch_id))
        print("-- NQL age >= 21, sort name, limit 10 --")
        for row in db.execute("db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)"):
            print(row.json)
        print("-- NQL $and London + active --")
        for row in db.execute(
            'db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })'
        ):
            print(row.json)
        print("-- NQL $or age < 30 or New York --")
        for row in db.execute(
            'db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })'
        ):
            print(row.json)
    print("encrypted", NuvexaDatabase.is_encrypted(path))
    try:
        NuvexaDatabase.open(path)
        raise SystemExit("open without key should fail")
    except NuvexaEncryptionException as ex:
        print("fail-closed", ex)


if __name__ == "__main__":
    main()
