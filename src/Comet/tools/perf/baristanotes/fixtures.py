#!/usr/bin/env python3
"""Generate the shared, local-only BaristaNotes comparison input."""

import argparse
from datetime import datetime, timedelta, timezone
import hashlib
import json
from pathlib import Path
import sys


VERSION = 2
COUNTS = (100, 1000)
REFERENCE_TIME = datetime(2026, 9, 1, 12, tzinfo=timezone.utc)


def timestamp(value):
    return value.isoformat(timespec="seconds").replace("+00:00", "Z")


def make_fixture(count):
    if count not in COUNTS:
        raise ValueError(f"Supported drink counts are {COUNTS}, not {count}")
    return {
        "version": VERSION,
        "name": f"barista-perf-v{VERSION}-{count}",
        "referenceTimeUtc": timestamp(REFERENCE_TIME),
        "drinkCount": count,
        "preferences": {
            "theme": "Light", "temperatureUnit": "Celsius",
            "valueRanges": "Auto", "activityFilter": "All",
        },
        "beans": [
            {
                "key": f"bean-{i:02}",
                "name": f"Benchmark coffee {i:02}",
                "roaster": f"Local benchmark {i:02}",
                "origin": "Benchmark",
                "notes": f"Generated local coffee {i:02}.",
            }
            for i in range(1, 6)
        ],
        "bags": [
            {
                "key": f"bag-{i:02}", "beanKey": f"bean-{i:02}",
                "roastDate": "2026-08-15", "isComplete": False,
                "notes": f"Generated local bag {i:02}.",
            }
            for i in range(1, 6)
        ],
        "equipment": [
            {
                "key": f"{kind.lower()}-{i:02}",
                "name": f"Benchmark {kind.lower()} {i:02}",
                "type": kind, "notes": "Generated local equipment.",
            }
            for kind in ("Machine", "Grinder")
            for i in range(1, 3)
        ],
        "people": [
            {"key": "person-01", "name": "Benchmark Alex"},
            {"key": "person-02", "name": "Benchmark Sam"},
        ],
        # Seed oldest first so recent-value preferences end at the newest drink.
        "drinks": [
            {
                "key": f"drink-{i:04}",
                "timestampUtc": timestamp(REFERENCE_TIME - timedelta(minutes=15 * (i - 1))),
                "bagKey": f"bag-{((i - 1) % 5) + 1:02}",
                "machineKey": f"machine-{((i - 1) % 2) + 1:02}",
                "grinderKey": f"grinder-{((i - 1) % 2) + 1:02}",
                "madeByKey": "person-01", "madeForKey": "person-02",
                "accessoryKeys": [], "brewMethod": "Espresso",
                "drinkType": "Espresso", "doseIn": 18, "grindMicrons": 270,
                "waterTempC": 93, "expectedTime": 28, "expectedOutput": 36,
                "actualTime": 28 + ((i - 1) % 3),
                "actualOutput": 36, "preinfusionTime": None,
                "rating": (i - 1) % 5,
                "tastingNotes": f"Generated tasting note {i:04}.",
            }
            for i in range(count, 0, -1)
        ],
    }


def encoded_fixture(fixture):
    return (json.dumps(fixture, sort_keys=True, separators=(",", ":"),
                       ensure_ascii=True, allow_nan=False) + "\n").encode("utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-directory", required=True, type=Path)
    args = parser.parse_args()
    try:
        args.output_directory.mkdir(parents=True, exist_ok=False)
        records = []
        for count in COUNTS:
            fixture = make_fixture(count)
            content = encoded_fixture(fixture)
            output = args.output_directory / f"{fixture['name']}.json"
            with output.open("xb") as stream:
                stream.write(content)
            records.append({
                "name": fixture["name"], "path": str(output),
                "drink_count": count, "bytes": len(content),
                "sha256": hashlib.sha256(content).hexdigest(),
            })
        print(json.dumps({"fixture_inputs": records}, indent=2))
        return 0
    except (OSError, ValueError) as error:
        print(f"Fixture generation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
