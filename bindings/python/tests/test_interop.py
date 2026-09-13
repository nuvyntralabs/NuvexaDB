import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from nuvexadb import NuvexaDatabase, NuvexaEncryptionException  # noqa: E402


def find_cases() -> Path:
    here = Path(__file__).resolve()
    for parent in here.parents:
        candidate = parent / "tests" / "interop" / "cases.json"
        if candidate.is_file():
            return candidate
    raise FileNotFoundError("tests/interop/cases.json was not found.")


@unittest.skipUnless(os.environ.get("NUVEXA_NATIVE_LIB"), "Set NUVEXA_NATIVE_LIB")
class InteropTests(unittest.TestCase):
    def test_golden_cases(self) -> None:
        fixture = json.loads(find_cases().read_text())
        with tempfile.TemporaryDirectory() as tmp:
            path = str(Path(tmp) / "golden.nvx")
            with NuvexaDatabase.create(path, fixture["key"]) as db:
                self._seed(db, fixture)
                self._assert_cases(db, fixture)
            self.assertTrue(NuvexaDatabase.is_encrypted(path))
            with self.assertRaises(NuvexaEncryptionException):
                NuvexaDatabase.open(path)
            with NuvexaDatabase.open(path, fixture["key"]) as db:
                self._assert_cases(db, fixture)

    def test_extended(self) -> None:
        self.assertEqual(NuvexaDatabase.abi_version(), 2)
        with tempfile.TemporaryDirectory() as tmp:
            path = str(Path(tmp) / "ext.nvx")
            with NuvexaDatabase.create(path, "key") as db:
                db.insert("users", json.dumps({"name": "Ada", "age": 36}))
                db.insert_many("users", json.dumps([{"name": "Ben", "age": 12}]))
                self.assertEqual(db.list_collections(), ["users"])
                self.assertEqual(db.count("users"), 2)
                db.begin_transaction()
                db.insert("users", json.dumps({"name": "Zoe", "age": 40}))
                db.rollback()
                self.assertEqual(db.count("users"), 2)

    def _seed(self, db: NuvexaDatabase, fixture: dict) -> None:
        for doc in fixture["documents"]:
            db.insert(fixture["collection"], json.dumps(doc))
        for fields in fixture["indexes"]:
            db.ensure_index(fixture["collection"], fields)

    def _assert_cases(self, db: NuvexaDatabase, fixture: dict) -> None:
        for query in fixture["cases"]:
            names = [doc.field("name") for doc in db.execute(query["nql"])]
            self.assertEqual(names, query["expectNames"], query["name"])


if __name__ == "__main__":
    unittest.main()
