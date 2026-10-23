"""Retain synthetic chat observations from DeviceRunners TRX, without machine paths."""

import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def collect(path, trial):
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    root = ET.parse(path).getroot()
    observations = []
    for result in root.findall(".//t:UnitTestResult", namespace):
        if "AppleIntelligenceChatClientGroundingTests." not in result.get("testName", ""):
            continue
        output = result.findtext("t:Output/t:StdOut", default="", namespaces=namespace)
        entries = [
            json.loads(line.removeprefix("APPLE_CHAT_EVALUATION "))
            for line in output.splitlines()
            if line.startswith("APPLE_CHAT_EVALUATION ")
        ]
        if len(entries) != 1:
            raise ValueError(f"Expected one observation for {result.get('testName')}")
        entry = entries[0]
        entry.update(
            trial=trial,
            outcome=result.get("outcome"),
            assertionFailure=result.findtext(
                "t:Output/t:ErrorInfo/t:Message", default="", namespaces=namespace
            ),
        )
        observations.append(entry)
    if not observations:
        raise ValueError(f"No chat evaluation observations in {path.name}")
    return observations


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trx", type=Path, nargs="+")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.resolve() in [path.resolve() for path in args.trx]:
        parser.error("Output must not overwrite an input TRX file")

    observations = [
        entry
        for trial, path in enumerate(args.trx, start=1)
        for entry in collect(path, trial)
    ]
    failures = sum(entry["outcome"] != "Passed" for entry in observations)
    result = {
        "schemaVersion": 1,
        "scope": "Synthetic local AppleIntelligenceChatClient quality probes, not model guarantees.",
        "total": len(observations),
        "failed": failures,
        "observations": observations,
    }
    args.output.write_text(json.dumps(result, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")
    print(f"Collected {len(observations)} observations; {failures} did not pass their ground-truth assertion.")
    print("Collection success is not a passing quality evaluation; inspect outcomes and assertionFailure.")


if __name__ == "__main__":
    main()
