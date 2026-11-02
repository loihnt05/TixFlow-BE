"""Negative controls for the handover verifier; standard library only."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("evidence", Path(__file__).with_name("Build-Phase13Evidence.py"))
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)


class ReviewIntegrityTests(unittest.TestCase):
    def archive(self, files, records=None):
        path = io.BytesIO()
        path.name = "review.zip"
        if records is None:
            records = [{"path": name, "bytes": len(body), "sha256": hashlib.sha256(body).hexdigest()} for name, body in files.items()]
        with zipfile.ZipFile(path, "w") as archive:
            for name, body in files.items():
                archive.writestr(name, body)
            archive.writestr("REVIEW-MANIFEST.json", json.dumps({"files": records}))
        path.seek(0)
        return path

    def test_valid_package(self):
        evidence.verify(self.archive({"backend/source.txt": b"original"}))

    def test_changed_content_rejected(self):
        record = {"path": "source.txt", "bytes": 7, "sha256": hashlib.sha256(b"correct").hexdigest()}
        with self.assertRaisesRegex(ValueError, "Hash mismatch"):
            evidence.verify(self.archive({"source.txt": b"changed"}, [record]))

    def test_missing_and_extra_files_rejected(self):
        record = {"path": "source.txt", "bytes": 1, "sha256": hashlib.sha256(b"x").hexdigest()}
        for files, records in (({}, [record]), ({"source.txt": b"x"}, [])):
            with self.subTest(files=list(files)), self.assertRaisesRegex(ValueError, "entries do not match"):
                evidence.verify(self.archive(files, records))

    def test_duplicate_manifest_rejected(self):
        record = {"path": "source.txt", "bytes": 1, "sha256": hashlib.sha256(b"x").hexdigest()}
        with self.assertRaisesRegex(ValueError, "Duplicate manifest"):
            evidence.verify(self.archive({"source.txt": b"x"}, [record, record]))

    def test_windows_collision_rejected(self):
        with self.assertRaisesRegex(ValueError, "case collisions"):
            evidence.verify(self.archive({"A.txt": b"a", "a.txt": b"b"}))

    def test_unsafe_or_secret_paths_rejected(self):
        for name in ("../outside", "/absolute", "C:/drive", "a\\b", "backend/.env.production", "backend/tokens.json", "backend/key.pem"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                evidence.verify(self.archive({name: b"fixture"}))

    def test_six_runs_from_one_variant_not_complete(self):
        self.assertFalse(evidence.complete_runs([f"baseline-{i}" for i in range(1, 7)], [{"passed": True}] * 6, 3))
        names = [f"{v}-{i}" for v in ("baseline", "optimized") for i in range(1, 4)]
        self.assertTrue(evidence.complete_runs(names, [{"passed": True}] * 6, 3))
        self.assertFalse(evidence.complete_runs(names, [{"passed": True}] * 5 + [{"passed": False}], 3))
        self.assertFalse(evidence.complete_runs(names, [{"passed": True}] * 5, 3))

    def test_empty_or_skipped_regression_rejected(self):
        for values in ((0, 0, 0, 0, 0), (2, 1, 1, 0, 1), (2, 2, 1, 1, 0)):
            with self.subTest(values=values), self.assertRaises(ValueError):
                evidence.validate_regression(dict(zip(("total", "executed", "passed", "failed", "notExecuted"), values)))
        evidence.validate_regression(dict(total=106, executed=106, passed=106, failed=0, notExecuted=0))


if __name__ == "__main__":
    unittest.main(verbosity=2)
