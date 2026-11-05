"""Run with: python3 -B -m unittest discover -s tools -p test_compose_art_profile.py -v"""

import hashlib
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
import warnings
import zipfile

from check_compose_art_profile import check_archive


APK_DIRECTORY = "assets/dexopt"
AAB_DIRECTORY = "BUNDLE-METADATA/com.android.tools.build.profiles"
# These are header/ZIP fixtures, not semantically decoded ART profiles.
PROFILE = b"pro\x00010\x00" + b"aaaa"
METADATA = b"prm\x00002\x00" + b"metadata body"


class ComposeArtProfileTests(unittest.TestCase):
    def setUp(self):
        # Never use the system temp directory; fixtures stay in the test cwd.
        self.workspace = tempfile.TemporaryDirectory(prefix=".compose-art-profile-tests-", dir=".")
        self.addCleanup(self.workspace.cleanup)
        self.root = Path(self.workspace.name)

    def make_archive(self, suffix=".apk", entries=None, compression=zipfile.ZIP_STORED):
        path = self.root / f"sample{suffix}"
        if entries is None:
            directory = AAB_DIRECTORY if suffix.lower() == ".aab" else APK_DIRECTORY
            entries = [(f"{directory}/baseline.prof", PROFILE),
                       (f"{directory}/baseline.profm", METADATA)]
        with zipfile.ZipFile(path, "w", compression=compression) as archive:
            for name, payload in entries:
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore", UserWarning)
                    archive.writestr(name, payload)
        return path

    def check_failure(self, path, message, expect_absent=False):
        report = check_archive(path, expect_absent)
        self.assertFalse(report["success"], report)
        self.assertIn(message, "\n".join(report["errors"]))
        self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(),
                         report["artifact"]["sha256"])
        self.assertEqual(path.stat().st_size, report["artifact"]["size_bytes"])
        self.assertIn("packaging only", report["scope"])
        return report

    def run_cli(self, *arguments):
        return subprocess.run(
            [sys.executable, "-B", str(Path(__file__).with_name("check_compose_art_profile.py")),
             *(str(argument) for argument in arguments)],
            capture_output=True, text=True, check=False,
        )

    def test_stored_apk_reports_sizes_headers_hash_and_scope(self):
        path = self.make_archive()
        before = path.read_bytes()
        report = check_archive(path)
        self.assertTrue(report["success"], report)
        self.assertEqual([], report["errors"])
        self.assertEqual("apk", report["artifact"]["type"])
        self.assertEqual(hashlib.sha256(before).hexdigest(), report["artifact"]["sha256"])
        self.assertEqual([len(PROFILE), len(METADATA)],
                         [entry["size_bytes"] for entry in report["entries"]])
        self.assertEqual(["010", "002"], [entry["format"] for entry in report["entries"]])
        self.assertEqual(["pro\x00", "prm\x00"], [entry["magic"] for entry in report["entries"]])
        for entry in report["entries"]:
            self.assertEqual(entry["size_bytes"], entry["bytes_read"])
            self.assertEqual(entry["size_bytes"], entry["compressed_size_bytes"])
            self.assertEqual(0, entry["compression_method"])
            self.assertEqual("ZIP_STORED", entry["compression"])
        self.assertIn("NOT applied ART compilation or DEX checksum compatibility", report["scope"])
        self.assertEqual(before, path.read_bytes())

    def test_aab_accepts_metadata_profiles_stored_or_deflated(self):
        for compression in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED):
            with self.subTest(compression=compression):
                report = check_archive(self.make_archive(".aab", compression=compression))
                self.assertTrue(report["success"], report)
                self.assertEqual("aab", report["artifact"]["type"])
                self.assertTrue(all(entry["path"].startswith(AAB_DIRECTORY + "/")
                                    for entry in report["entries"]))

    def test_known_android_profile_and_metadata_versions_are_accepted(self):
        for version in (b"001\x00", b"005\x00", b"009\x00", b"010\x00", b"015\x00"):
            for metadata_version in (b"001\x00", b"002\x00"):
                with self.subTest(version=version, metadata=metadata_version):
                    entries = [
                        (f"{APK_DIRECTORY}/baseline.prof", b"pro\x00" + version + b"body"),
                        (f"{APK_DIRECTORY}/baseline.profm", b"prm\x00" + metadata_version + b"body"),
                    ]
                    report = check_archive(self.make_archive(entries=entries))
                    self.assertTrue(report["success"], report)

    def test_missing_profiles_fail_for_both_archive_types(self):
        for suffix in (".apk", ".aab"):
            with self.subTest(suffix=suffix):
                report = self.check_failure(self.make_archive(suffix, []), "Missing profile entry")
                self.assertEqual(2, len(report["errors"]))

    def test_either_partial_pair_fails(self):
        for suffix, directory in ((".apk", APK_DIRECTORY), (".aab", AAB_DIRECTORY)):
            for name, payload in (("baseline.prof", PROFILE), ("baseline.profm", METADATA)):
                with self.subTest(suffix=suffix, name=name):
                    path = self.make_archive(suffix, [(f"{directory}/{name}", payload)])
                    report = self.check_failure(path, "partial or misplaced")
                    self.assertEqual(1, len(report["entries"]))

    def test_duplicate_profiles_fail_and_all_occurrences_are_reported(self):
        for suffix, directory in ((".apk", APK_DIRECTORY), (".aab", AAB_DIRECTORY)):
            for duplicate, payload in (("baseline.prof", PROFILE), ("baseline.profm", METADATA)):
                with self.subTest(suffix=suffix, duplicate=duplicate):
                    entries = [(f"{directory}/baseline.prof", PROFILE),
                               (f"{directory}/baseline.profm", METADATA),
                               (f"{directory}/{duplicate}", payload)]
                    report = self.check_failure(self.make_archive(suffix, entries), "Duplicate profile entry")
                    self.assertEqual(3, len(report["entries"]))

    def test_deflated_apk_fails_even_when_compressed_and_raw_lengths_match(self):
        path = self.make_archive(compression=zipfile.ZIP_DEFLATED)
        report = self.check_failure(path, "must use ZIP_STORED")
        profile = report["entries"][0]
        self.assertEqual(profile["size_bytes"], profile["compressed_size_bytes"])
        self.assertEqual(zipfile.ZIP_DEFLATED, profile["compression_method"])
        self.assertEqual("010", profile["format"])

    def test_compressing_only_metadata_also_fails(self):
        path = self.make_archive(entries=[(f"{APK_DIRECTORY}/baseline.prof", PROFILE)])
        with zipfile.ZipFile(path, "a", compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr(f"{APK_DIRECTORY}/baseline.profm", METADATA)
        self.check_failure(path, "baseline.profm: APK profile entry must use ZIP_STORED")

    def test_wrong_magic_fails_for_either_entry_with_format_evidence(self):
        for name, payload in (("baseline.prof", PROFILE), ("baseline.profm", METADATA)):
            with self.subTest(name=name):
                entries = [(f"{APK_DIRECTORY}/baseline.prof", PROFILE),
                           (f"{APK_DIRECTORY}/baseline.profm", METADATA)]
                entries = [(path, b"bad!" + data[4:] if path.endswith("/" + name) else data)
                           for path, data in entries]
                report = self.check_failure(self.make_archive(entries=entries), "Invalid profile magic")
                entry = next(entry for entry in report["entries"] if entry["path"].endswith("/" + name))
                self.assertEqual("bad!", entry["magic"])
                self.assertEqual(payload[4:7].decode("ascii"), entry["format"])

    def test_unsupported_and_non_null_terminated_format_headers_fail(self):
        for name, magic in (("baseline.prof", b"pro\x00"), ("baseline.profm", b"prm\x00")):
            for version in (b"999\x00", b"010x", b"\xff\x00\x01\x00"):
                with self.subTest(name=name, version=version):
                    entries = [(f"{APK_DIRECTORY}/baseline.prof", PROFILE),
                               (f"{APK_DIRECTORY}/baseline.profm", METADATA)]
                    entries = [(path, magic + version + b"body" if path.endswith("/" + name) else data)
                               for path, data in entries]
                    report = self.check_failure(self.make_archive(entries=entries), "Unsupported profile format")
                    entry = next(entry for entry in report["entries"] if entry["path"].endswith("/" + name))
                    self.assertEqual(version.hex(), entry["format_hex"])
                    json.dumps(report)

    def test_empty_truncated_headers_and_header_only_assets_fail(self):
        for name, payload in (("baseline.prof", PROFILE), ("baseline.profm", METADATA)):
            for length in range(9):
                with self.subTest(name=name, length=length):
                    entries = [(f"{APK_DIRECTORY}/baseline.prof", PROFILE),
                               (f"{APK_DIRECTORY}/baseline.profm", METADATA)]
                    entries = [(path, data[:length] if path.endswith("/" + name) else data)
                               for path, data in entries]
                    message = "empty" if length == 0 else "Truncated profile"
                    report = self.check_failure(self.make_archive(entries=entries), message)
                    entry = next(entry for entry in report["entries"] if entry["path"].endswith("/" + name))
                    self.assertEqual(length, entry["size_bytes"])
                    if length > 4:
                        self.assertEqual(payload[4:length].hex(), entry["format_hex"])

    def test_profiles_under_wrong_archive_path_fail(self):
        for suffix, directory in ((".apk", AAB_DIRECTORY), (".aab", APK_DIRECTORY)):
            with self.subTest(suffix=suffix):
                entries = [(f"{directory}/baseline.prof", PROFILE),
                           (f"{directory}/baseline.profm", METADATA)]
                self.check_failure(self.make_archive(suffix, entries), "Misplaced profile entry")

    def test_extra_misplaced_profile_fails_even_with_correct_pair(self):
        entries = [(f"{APK_DIRECTORY}/baseline.prof", PROFILE),
                   (f"{APK_DIRECTORY}/baseline.profm", METADATA),
                   ("other/baseline.prof", PROFILE)]
        self.check_failure(self.make_archive(entries=entries), "Misplaced profile entry")

    def test_directory_cannot_substitute_for_profile(self):
        entries = [(f"{APK_DIRECTORY}/baseline.prof/", b""),
                   (f"{APK_DIRECTORY}/baseline.profm", METADATA)]
        self.check_failure(self.make_archive(entries=entries), "Profile entry is a directory")

    def test_expect_absent_passes_without_profiles_and_preserves_archive(self):
        for suffix in (".apk", ".aab"):
            with self.subTest(suffix=suffix):
                path = self.make_archive(suffix, [("assets/other.txt", b"unrelated")])
                before = path.read_bytes()
                report = check_archive(path, expect_absent=True)
                self.assertTrue(report["success"], report)
                self.assertEqual([], report["entries"])
                self.assertEqual(hashlib.sha256(before).hexdigest(), report["artifact"]["sha256"])
                self.assertEqual(before, path.read_bytes())

    def test_expect_absent_rejects_stale_complete_pair(self):
        for suffix in (".apk", ".aab"):
            with self.subTest(suffix=suffix):
                report = self.check_failure(self.make_archive(suffix), "Unexpected profile entry", True)
                self.assertEqual(["010", "002"], [entry["format"] for entry in report["entries"]])

    def test_expect_absent_rejects_partial_empty_misplaced_and_directory_artifacts(self):
        for name in (f"{APK_DIRECTORY}/baseline.prof", f"{AAB_DIRECTORY}/baseline.profm",
                     "old/baseline.prof", "assets\\dexopt\\baseline.profm", "old/baseline.prof/"):
            with self.subTest(name=name):
                self.check_failure(self.make_archive(entries=[(name, b"")]),
                                   "forbids stale or partial profile artifacts", True)

    def test_unsupported_suffix_reports_hash_and_available_profile_information(self):
        for suffix in (".zip", ".apk.zip", ""):
            with self.subTest(suffix=suffix):
                report = self.check_failure(self.make_archive(suffix), "Unsupported artifact suffix")
                self.assertIsNone(report["artifact"]["type"])
                self.assertEqual(2, len(report["entries"]))
                self.assertEqual("010", report["entries"][0]["format"])

    def test_uppercase_supported_suffix_is_accepted(self):
        self.assertTrue(check_archive(self.make_archive(".APK"))["success"])

    def test_corrupt_zip_fails_even_in_expect_absent_mode(self):
        path = self.root / "corrupt.apk"
        path.write_bytes(b"not a ZIP archive")
        for expect_absent in (False, True):
            with self.subTest(expect_absent=expect_absent):
                self.check_failure(path, "BadZipFile", expect_absent)

    def test_truncated_zip_directory_fails(self):
        path = self.make_archive()
        path.write_bytes(path.read_bytes()[:-22])
        self.check_failure(path, "BadZipFile")

    def test_profile_payload_crc_corruption_fails(self):
        path = self.make_archive()
        content = bytearray(path.read_bytes())
        with zipfile.ZipFile(path) as archive:
            info = archive.infolist()[0]
            name_length, extra_length = struct.unpack_from("<HH", content, info.header_offset + 26)
            payload_offset = info.header_offset + 30 + name_length + extra_length
        content[payload_offset + 8] ^= 0x01
        path.write_bytes(content)
        report = self.check_failure(path, "Corrupt or unreadable ZIP payload")
        self.assertEqual(len(PROFILE), report["entries"][0]["size_bytes"])
        self.assertEqual("002", report["entries"][1]["format"])

    def test_missing_file_and_directory_fail_without_exception(self):
        for path in (self.root / "missing.apk", self.root):
            with self.subTest(path=path):
                report = check_archive(path)
                self.assertFalse(report["success"])
                self.assertIn("Cannot inspect archive", "\n".join(report["errors"]))
                self.assertIsNone(report["artifact"]["sha256"])

    def test_cli_json_success_and_failure_exit_codes(self):
        for suffix in (".apk", ".aab"):
            with self.subTest(suffix=suffix):
                path = self.make_archive(suffix)
                result = self.run_cli(path, "--json")
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertTrue(json.loads(result.stdout)["success"])
                result = self.run_cli(path, "--expect-absent", "--json")
                self.assertEqual(1, result.returncode, result.stderr)
                report = json.loads(result.stdout)
                self.assertFalse(report["success"])
                self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), report["artifact"]["sha256"])
                self.assertEqual(2, len(report["entries"]))

    def test_cli_expect_absent_success_and_missing_pair_failure(self):
        path = self.make_archive(entries=[])
        result = self.run_cli(path, "--expect-absent", "--json")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(json.loads(result.stdout)["expect_absent"])
        result = self.run_cli(path, "--json")
        self.assertEqual(1, result.returncode, result.stderr)
        self.assertFalse(json.loads(result.stdout)["success"])

    def test_cli_corrupt_zip_and_unsupported_suffix_fail_with_json_not_traceback(self):
        paths = [self.make_archive(".zip"), self.root / "corrupt.aab", self.root / "missing.apk"]
        paths[1].write_bytes(b"invalid ZIP")
        for path in paths:
            with self.subTest(path=path):
                result = self.run_cli(path, "--json")
                self.assertEqual(1, result.returncode, result.stderr)
                self.assertFalse(json.loads(result.stdout)["success"])
                self.assertEqual("", result.stderr)

    def test_cli_text_success_and_failure_include_scope(self):
        path = self.make_archive()
        for arguments, status, code in (([], "PASS", 0), (["--expect-absent"], "FAIL", 1)):
            with self.subTest(arguments=arguments):
                result = self.run_cli(path, *arguments)
                self.assertEqual(code, result.returncode, result.stderr)
                self.assertIn(status, result.stdout)
                self.assertIn("SHA-256:", result.stdout)
                self.assertIn("packaging only, NOT applied ART compilation or DEX checksum compatibility",
                              result.stdout)

    def test_cli_usage_errors_return_two(self):
        for arguments in ([], ["--unknown"], ["missing.apk", "--unknown"]):
            with self.subTest(arguments=arguments):
                result = self.run_cli(*arguments)
                self.assertEqual(2, result.returncode)
                self.assertIn("usage:", result.stderr)


if __name__ == "__main__":
    unittest.main()
