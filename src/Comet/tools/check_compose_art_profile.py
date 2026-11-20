#!/usr/bin/env python3
"""Check ART baseline-profile packaging without installing or modifying an archive.

Usage:
    python3 tools/check_compose_art_profile.py path/to/app.apk --json
    python3 tools/check_compose_art_profile.py path/to/app.aab --expect-absent

This checks ZIP payload integrity and recognized headers, not ART profile-body
semantics, DEX checksums, or whether a device has compiled anything.
Exit codes: 0 = packaging check passed, 1 = failed, 2 = invalid CLI arguments.
"""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import sys
import zipfile
import zlib


SCOPE = (
    "This checks packaging only, NOT applied ART compilation or DEX checksum "
    "compatibility. ART profile-body semantics are not validated."
)
PROFILE_DIRECTORIES = {
    "apk": "assets/dexopt",
    "aab": "BUNDLE-METADATA/com.android.tools.build.profiles",
}
# AndroidX ProfileVersion: N, O, O MR1, P, S; metadata versions N and S.
# A known header is not evidence that the body is suitable for a particular DEX.
PROFILE_FORMATS = {
    "baseline.prof": (b"pro\x00", (b"001\x00", b"005\x00", b"009\x00",
                                 b"010\x00", b"015\x00")),
    "baseline.profm": (b"prm\x00", (b"001\x00", b"002\x00")),
}
COMPRESSION_NAMES = {
    zipfile.ZIP_STORED: "ZIP_STORED",
    zipfile.ZIP_DEFLATED: "ZIP_DEFLATED",
    zipfile.ZIP_BZIP2: "ZIP_BZIP2",
    zipfile.ZIP_LZMA: "ZIP_LZMA",
}
READ_ERRORS = (OSError, EOFError, ValueError, RuntimeError, NotImplementedError,
               zipfile.BadZipFile, zipfile.LargeZipFile, zlib.error)
CHUNK_SIZE = 1024 * 1024


def profile_name(path):
    # Also report misplaced Windows-style paths and directory entries rather
    # than letting an opt-out check hide stale profile artifacts.
    name = path.replace("\\", "/").rstrip("/").rsplit("/", 1)[-1]
    return name if name in PROFILE_FORMATS else None


def inspect_entry(archive, info, artifact_type):
    entry = {
        "path": info.filename,
        "size_bytes": info.file_size,
        "compressed_size_bytes": info.compress_size,
        "compression_method": info.compress_type,
        "compression": COMPRESSION_NAMES.get(info.compress_type, "UNKNOWN"),
        "crc32": f"{info.CRC:08x}",
        "bytes_read": 0,
        "magic": None,
        "format": None,
        "format_hex": None,
        "errors": [],
    }
    errors = entry["errors"]
    if info.is_dir():
        errors.append("Profile entry is a directory, not a file")
    if artifact_type == "apk" and info.compress_type != zipfile.ZIP_STORED:
        errors.append("APK profile entry must use ZIP_STORED (compression method 0)")

    header = bytearray()
    try:
        with archive.open(info) as stream:
            while chunk := stream.read(CHUNK_SIZE):
                entry["bytes_read"] += len(chunk)
                header.extend(chunk[:max(0, 8 - len(header))])
    except READ_ERRORS as error:
        errors.append(f"Corrupt or unreadable ZIP payload: {type(error).__name__}: {error}")

    if header:
        entry["magic"] = bytes(header[:4]).decode("ascii", errors="backslashreplace")
    if len(header) > 4:
        version = bytes(header[4:8])
        entry["format_hex"] = version.hex()
        entry["format"] = (
            version[:-1] if len(version) == 4 and version[-1] == 0 else version
        ).decode("ascii", errors="backslashreplace")

    if entry["bytes_read"] != info.file_size:
        errors.append(
            f"Truncated or corrupt payload: read {entry['bytes_read']} bytes, "
            f"ZIP directory declares {info.file_size}"
        )
    if not header:
        if info.file_size == 0:
            errors.append("Profile entry is empty")
        return entry
    if len(header) < 8:
        errors.append(f"Truncated profile header: expected 8 bytes, found {len(header)}")
        return entry

    magic, formats = PROFILE_FORMATS[profile_name(info.filename)]
    if header[:4] != magic:
        errors.append(f"Invalid profile magic: expected {magic!r}, found {bytes(header[:4])!r}")
    if bytes(header[4:8]) not in formats:
        supported = ", ".join(version[:3].decode("ascii") for version in formats)
        errors.append(
            f"Unsupported profile format header {bytes(header[4:8])!r}; "
            f"supported versions: {supported}"
        )
    if entry["bytes_read"] == 8:
        errors.append("Truncated profile payload: header is present but body is missing")
    return entry


def check_archive(path, expect_absent=False):
    """Return a JSON-serializable report, retaining available evidence on failure."""
    path = Path(path)
    artifact_type = path.suffix.lower().lstrip(".")
    directory = PROFILE_DIRECTORIES.get(artifact_type)
    expected = [f"{directory}/{name}" for name in PROFILE_FORMATS] if directory else []
    report = {
        "schema_version": 1,
        "success": False,
        "artifact": {
            "path": str(path),
            "type": artifact_type if directory else None,
            "size_bytes": None,
            "sha256": None,
        },
        "expect_absent": expect_absent,
        "expected_entries": expected,
        "entries": [],
        "errors": [],
        "scope": SCOPE,
    }
    errors = report["errors"]
    if directory is None:
        errors.append(f"Unsupported artifact suffix {path.suffix!r}: expected .apk or .aab")

    try:
        with path.open("rb") as stream:
            digest = hashlib.sha256()
            size = 0
            while chunk := stream.read(CHUNK_SIZE):
                digest.update(chunk)
                size += len(chunk)
            report["artifact"]["size_bytes"] = size
            report["artifact"]["sha256"] = digest.hexdigest()
            stream.seek(0)
            with zipfile.ZipFile(stream) as archive:
                profiles = [info for info in archive.infolist()
                            if profile_name(info.filename) is not None]
                counts = Counter(info.filename for info in profiles)
                for name, count in counts.items():
                    if count > 1:
                        errors.append(f"Duplicate profile entry {name!r}: found {count}, expected at most 1")
                if not expect_absent:
                    for name in expected:
                        if counts[name] == 0:
                            detail = " (partial or misplaced profile pair)" if profiles else ""
                            errors.append(f"Missing profile entry {name!r}{detail}")
                for info in profiles:
                    if expect_absent:
                        errors.append(
                            f"Unexpected profile entry {info.filename!r}: "
                            "--expect-absent forbids stale or partial profile artifacts"
                        )
                    elif directory and info.filename not in expected:
                        errors.append(f"Misplaced profile entry {info.filename!r}; expected paths: {expected}")
                    entry = inspect_entry(archive, info, artifact_type)
                    report["entries"].append(entry)
                    errors.extend(f"{info.filename}: {error}" for error in entry["errors"])
    except READ_ERRORS as error:
        errors.append(f"Cannot inspect archive: {type(error).__name__}: {error}")

    report["success"] = not errors
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("artifact", type=Path, help="APK or AAB archive to inspect read-only")
    parser.add_argument("--expect-absent", action="store_true",
                        help="Require no baseline.prof or baseline.profm entries, including misplaced ones")
    parser.add_argument("--json", action="store_true", help="Print the complete report as JSON, including failures")
    args = parser.parse_args(argv)
    report = check_archive(args.artifact, args.expect_absent)
    if args.json:
        print(json.dumps(report, indent=2))
    else:
        status = "PASS" if report["success"] else "FAIL"
        expectation = "profiles absent" if args.expect_absent else "profiles present"
        print(f"{status}: {args.artifact} ({expectation})")
        print(f"SHA-256: {report['artifact']['sha256'] or 'unavailable'}")
        for entry in report["entries"]:
            print(f"  {entry['path']}: {entry['size_bytes']} bytes, "
                  f"stored size {entry['compressed_size_bytes']} bytes, "
                  f"{entry['compression']}, format {entry['format']!r}")
        for error in report["errors"]:
            print(f"  ERROR: {error}")
        print(report["scope"])
    return 0 if report["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
