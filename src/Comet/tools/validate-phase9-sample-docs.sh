#!/usr/bin/env bash

set -euo pipefail

usage() {
	cat <<'EOF'
Usage:
  tools/validate-phase9-sample-docs.sh \
    --counter-sample path/to/CounterSample.csproj \
    --coffee-sample path/to/CoffeeApp.csproj \
    --migration-guide path/to/migration-guide.md

This gate follows the documented Comet build order, then runs the focused
Phase 9 sample/documentation validation tests against the supplied artifacts.
EOF
}

require_file() {
	local path="$1"
	local label="$2"

	if [[ ! -f "$path" ]]; then
		echo "Missing ${label}: $path" >&2
		exit 1
	fi
}

require_maccatalyst_target() {
	local project_path="$1"
	local label="$2"

	if ! grep -q "net10.0-maccatalyst" "$project_path"; then
		echo "${label} must target net10.0-maccatalyst for the macOS validation lane: $project_path" >&2
		exit 1
	fi
}

counter_sample=""
coffee_sample=""
migration_guide=""
configuration="Release"

while [[ $# -gt 0 ]]; do
	case "$1" in
		--counter-sample)
			counter_sample="$2"
			shift 2
			;;
		--coffee-sample)
			coffee_sample="$2"
			shift 2
			;;
		--migration-guide)
			migration_guide="$2"
			shift 2
			;;
		--configuration)
			configuration="$2"
			shift 2
			;;
		-h|--help)
			usage
			exit 0
			;;
		*)
			echo "Unknown argument: $1" >&2
			usage >&2
			exit 1
			;;
	esac
done

if [[ -z "$counter_sample" || -z "$coffee_sample" || -z "$migration_guide" ]]; then
	usage >&2
	exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "${script_dir}/.." && pwd)"

counter_sample="$(cd -- "$(dirname -- "$counter_sample")" && pwd)/$(basename -- "$counter_sample")"
coffee_sample="$(cd -- "$(dirname -- "$coffee_sample")" && pwd)/$(basename -- "$coffee_sample")"
migration_guide="$(cd -- "$(dirname -- "$migration_guide")" && pwd)/$(basename -- "$migration_guide")"

require_file "$counter_sample" "counter sample project"
require_file "$coffee_sample" "coffee app project"
require_file "$migration_guide" "migration guide"
require_maccatalyst_target "$counter_sample" "Counter sample"
require_maccatalyst_target "$coffee_sample" "Coffee app"

echo "==> Building Comet validation prerequisites"
dotnet build "${repo_root}/src/Comet.SourceGenerator/Comet.SourceGenerator.csproj" -c "$configuration"
dotnet build "${repo_root}/src/Comet/Comet.csproj" -c "$configuration"
dotnet build "${repo_root}/tests/Comet.Tests/Comet.Tests.csproj" -c "$configuration"

echo "==> Building Phase 9 sample artifacts"
dotnet build "$counter_sample" -c "$configuration" -f net10.0-maccatalyst
dotnet build "$coffee_sample" -c "$configuration" -f net10.0-maccatalyst

echo "==> Running focused Phase 9 validation gate"
COMET_PHASE9_COUNTER_SAMPLE_PROJECT="$counter_sample" \
COMET_PHASE9_COFFEE_SAMPLE_PROJECT="$coffee_sample" \
COMET_PHASE9_MIGRATION_GUIDE="$migration_guide" \
dotnet test "${repo_root}/tests/Comet.Tests/Comet.Tests.csproj" \
	--no-build \
	-c "$configuration" \
	--filter "FullyQualifiedName~Phase9SampleDocumentationValidationTests"
