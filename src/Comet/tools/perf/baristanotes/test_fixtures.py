import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from fixtures import encoded_fixture, make_fixture


class FixtureTests(unittest.TestCase):
    def test_counts_and_relationships(self):
        for count in (100, 1000):
            with self.subTest(count=count):
                fixture = make_fixture(count)
                self.assertEqual(count, len(fixture["drinks"]))
                self.assertEqual(count, len({row["key"] for row in fixture["drinks"]}))
                bags = {row["key"] for row in fixture["bags"]}
                equipment = {row["key"] for row in fixture["equipment"]}
                people = {row["key"] for row in fixture["people"]}
                for row in fixture["drinks"]:
                    self.assertIn(row["bagKey"], bags)
                    self.assertIn(row["machineKey"], equipment)
                    self.assertIn(row["grinderKey"], equipment)
                    self.assertIn(row["madeByKey"], people)
                    self.assertIn(row["madeForKey"], people)
                    self.assertIn(row["rating"], range(5))

    def test_output_is_deterministic_and_ascii(self):
        first = encoded_fixture(make_fixture(100))
        second = encoded_fixture(make_fixture(100))
        self.assertEqual(first, second)
        self.assertEqual(hashlib.sha256(first).digest(), hashlib.sha256(second).digest())
        self.assertEqual(first, first.decode("ascii").encode("ascii"))

    def test_larger_fixture_preserves_the_latest_hundred_drinks(self):
        small = make_fixture(100)
        large = make_fixture(1000)
        self.assertEqual(small["drinks"], large["drinks"][-100:])
        for key in ("preferences", "beans", "bags", "equipment", "people"):
            self.assertEqual(small[key], large[key])
        self.assertEqual(small["referenceTimeUtc"], small["drinks"][-1]["timestampUtc"])

    def test_seed_order_is_oldest_first(self):
        drinks = make_fixture(1000)["drinks"]
        dates = [row["timestampUtc"] for row in drinks]
        self.assertEqual(sorted(dates), dates)

    def test_version_two_requires_explicit_null_preinfusion(self):
        for count in (100, 1000):
            with self.subTest(count=count):
                fixture = json.loads(encoded_fixture(make_fixture(count)))
                self.assertEqual(2, fixture["version"])
                self.assertEqual(f"barista-perf-v2-{count}", fixture["name"])
                for row in fixture["drinks"]:
                    self.assertIsNone(row["preinfusionTime"])

    def test_cli_writes_versioned_files_and_matching_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "fixtures-v2"
            result = self.run_generator(output)
            self.assertEqual(0, result.returncode, result.stderr)
            records = json.loads(result.stdout)["fixture_inputs"]
            self.assertEqual([100, 1000], [row["drink_count"] for row in records])
            for record in records:
                count = record["drink_count"]
                path = output / f"barista-perf-v2-{count}.json"
                content = path.read_bytes()
                self.assertEqual(str(path), record["path"])
                self.assertEqual(f"barista-perf-v2-{count}", record["name"])
                self.assertEqual(encoded_fixture(make_fixture(count)), content)
                self.assertEqual(len(content), record["bytes"])
                self.assertEqual(hashlib.sha256(content).hexdigest(), record["sha256"])

    def test_cli_rejects_existing_directory_without_changing_data(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            existing = output / "barista-perf-v1-100.json"
            original = b"preserved prior fixture\n"
            existing.write_bytes(original)
            result = self.run_generator(output)
            self.assertEqual(1, result.returncode)
            self.assertIn("Fixture generation failed:", result.stderr)
            self.assertEqual(original, existing.read_bytes())
            self.assertEqual([existing], list(output.iterdir()))

    @staticmethod
    def run_generator(output):
        return subprocess.run(
            [sys.executable, "-B", str(Path(__file__).with_name("fixtures.py")),
             "--output-directory", str(output)],
            capture_output=True, text=True, check=False,
        )

    def test_unapproved_sizes_fail(self):
        for count in (-1, 0, 10, 101):
            with self.subTest(count=count), self.assertRaises(ValueError):
                make_fixture(count)


if __name__ == "__main__":
    unittest.main()
