import importlib.util
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile

from run import HOST, load_manifest, validate_result


spec = importlib.util.spec_from_file_location("bundle_resources_driver", HOST / "Scenarios/bundle-resources/driver.py")
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)


class PackageConsumerContractTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()

    def package(self, package_id, version="0.1.0-ci.1.1", folder="packages with spaces"):
        path = self.root / folder / f"{package_id}.{version}.nupkg"
        path.parent.mkdir(parents=True, exist_ok=True)
        with ZipFile(path, "w") as archive:
            archive.writestr(f"{package_id}.nuspec",
                             f'<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
                             f'<metadata><id>{package_id}</id><version>{version}</version></metadata></package>')
        return path

    def test_package_mode_uses_both_actual_nuspec_versions(self):
        for package_id in driver.PACKAGE_IDS:
            self.package(package_id)
        properties = driver.package_properties(self.root)
        self.assertEqual(properties["ResourceTestPackageVersion"], "0.1.0-ci.1.1")
        self.assertEqual(properties["RuntimeTestsUseProjectReferences"], "false")
        self.assertEqual(properties["RestoreAdditionalProjectSources"], str(self.root / "packages with spaces"))

    def test_missing_essential_package_fails(self):
        self.package(driver.PACKAGE_IDS[0])
        with self.assertRaisesRegex(ValueError, "core and Essentials"):
            driver.package_properties(self.root)

    def test_mismatched_versions_fail(self):
        for index, package_id in enumerate(driver.PACKAGE_IDS):
            self.package(package_id, f"0.1.{index}")
        with self.assertRaisesRegex(ValueError, "versions must match"):
            driver.package_properties(self.root)

    def test_duplicate_versions_fail_instead_of_picking_one(self):
        self.package(driver.PACKAGE_IDS[0], "0.1.0")
        self.package(driver.PACKAGE_IDS[0], "0.1.1")
        with self.assertRaisesRegex(ValueError, "Ambiguous package"):
            driver.package_properties(self.root)

    def test_reference_evidence_requires_each_matching_package_once(self):
        evidence = self.root / "references.txt"
        evidence.write_text(f"{driver.PACKAGE_IDS[0]}|0.1.0\n{driver.PACKAGE_IDS[1]}|0.1.0\n")
        driver.assert_package_references(evidence, "0.1.0")
        with self.assertRaises(ValueError):
            driver.assert_package_references(evidence, "0.1.1")

    def test_baseline_requires_specific_observed_regression(self):
        manifest = load_manifest("bundle-resources")
        baseline = manifest["baseline"]
        result = {
            "scenario": manifest["name"], "expectedCases": 4, "expectedAssertions": 26,
            "assertions": 9, "cases": 0, "exitCode": 42, "outcome": "regression",
            "failureId": baseline["failureId"], "message": baseline["message"],
        }
        validate_result(manifest, result, 42, baseline)
        result["message"] = "An unrelated startup error"
        with self.assertRaises(ValueError):
            validate_result(manifest, result, 42, baseline)


if __name__ == "__main__":
    unittest.main()
