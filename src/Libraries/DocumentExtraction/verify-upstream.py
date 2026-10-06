import hashlib
import json
from pathlib import Path


root = Path(__file__).resolve().parent
manifest = json.loads((root / "upstream-manifest.json").read_text())
expected = {entry["path"] for entry in manifest["files"]}
actual = {str(path.relative_to(root / "Upstream")) for path in (root / "Upstream").rglob("*") if path.is_file()}
if actual != expected:
    raise SystemExit(f"Upstream file set changed: missing={sorted(expected - actual)}, extra={sorted(actual - expected)}")

for entry in manifest["files"]:
    content = (root / "Upstream" / entry["path"]).read_bytes()
    digest = hashlib.sha256(content).hexdigest()
    if digest != entry["sha256"]:
        raise SystemExit(f"Upstream content changed: {entry['path']}")

print(f"Verified {len(expected)} verbatim artifacts from dotnet/extensions {manifest['commit']}.")
