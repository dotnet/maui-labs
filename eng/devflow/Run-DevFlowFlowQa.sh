#!/usr/bin/env bash
# Checked-in DevFlow flow-QA host entry point. It intentionally never installs SDKs,
# workloads, Xcode, Appium, or device tooling.
set -uo pipefail

EXIT_SUCCESS=0
EXIT_USAGE=2
EXIT_PREREQUISITE=3
EXIT_FLOW_FAILURE=4
EXIT_PENDING=5
MAX_ARTIFACTS=256
MAX_DIAGNOSTIC_BYTES=65536
MAX_DIAGNOSTIC_LINES=1000
# The unfinalized flow-pilot manifest is preserved beside the fallback when finalization fails, and
# a manifest larger than this is not copied: an artifact this pass cannot bound is not published.
MAX_PRESERVED_MANIFEST_BYTES=4194304
UNFINALIZED_MANIFEST_NAME=manifest.unfinalized.json
# The last resort, and the only one that reads free text. It answers solely when the host produced
# no structured failure evidence at all, so every marker is an anchored phrase a failing host
# actually prints. A bare word could not stay: an ordinary line naming the emulator, or a flow
# assertion that timed out, reclassified a product defect as an infrastructure failure and
# excused it. Kept byte-identical to $script:InfrastructureDiagnosticPattern in
# Run-DevFlowFlowQa.ps1 apart from that file's leading case-insensitivity flag.
INFRASTRUCTURE_DIAGNOSTIC_PATTERN='\b(workload.{0,64}(is|are) not installed|to install missing workloads|workload manifest .{0,64}not found|dotnet sdk .{0,40}not found|sdk .{0,40}was not found|adb(\.exe)?:? .{0,40}not found|adb: no devices|no devices?/emulators? found|device .{0,40}not found|emulator: error|emulator .{0,40}(failed to start|failed to boot|terminated|exited)|avd .{0,40}(not found|does not exist)|xcrun: error|simctl .{0,40}(error|failed)|unable to boot device|agent readiness (timed out|failed)|(emulator|simulator|avd|adb|device readiness|agent readiness)( [a-z]+){0,3} timed out|(agent|emulator|simulator|device|broker|fixture) did not become ready|fixture initialization (failed|error)|android-fixture-initialization|infrastructure-error|infrastructure-failure)\b'

usage() {
  cat <<'EOF'
Usage:
  Run-DevFlowFlowQa.sh --platform android|windows|ios|maccatalyst|macos \
    --results-root <repo>/artifacts/TestResults/devflow-flow/<platform> [options]

Required:
  --platform <name>       android, windows, ios, maccatalyst, or macos
  --results-root <path>   Exact repository-local results directory for the selected platform

Options:
  --repeat <N>            Clean repetitions per invocation (default: 3; maximum: 20).
                          The cap is deliberate: gates that need 100+ clean first attempts want
                          100 independent runs, not 100 iterations of one warm process. Use
                          --accumulate to merge evidence across separate runs instead.
  --accumulate <dir>      Merge qualification metric numerators/denominators across independent
                          runs into <dir>. Requires --qualification.
  --baseline <path>       Fail when a gated qualification metric regresses below this committed
                          baseline report. Requires --qualification.
  --configuration <name>  Test configuration (default: Debug)
  --flow-filter <filter>  Additional VSTest filter appended to the platform filter
  --no-build              Pass --no-build to dotnet test
  --qualification         Run the read-only qualification evaluator after the flow host
  --experimental          Required for the experimental AppKit/macOS lane
  --physical-device       Run the separately identified physical-iOS lane
  --device-id <id>        Android serial or required physical-iOS device identifier
  --ios-runtime <version> iOS Simulator runtime selector (for ios simulator only)
  --signing-identity <id> Physical-iOS signing identity; never written to artifacts
  --provisioning-profile <id>
                           Physical-iOS provisioning profile; never written to artifacts
  --keychain <path>       Physical-iOS keychain reference; never written to artifacts
  --apple-spike           Explicitly request the required XCTest/XCUITest capability proof
  --target-app <path>     Optional prebuilt instrumented .app; otherwise the sample is built
  --target-bundle-id <id> Approved target bundle identifier (default: com.companyname.mauitodo)
  --simulator-id <id>     iOS Simulator UDID; otherwise an available iPhone is selected
  --safe-action-id <id>   Safe proof action identifier (default: AddButton)
  --apple-in-app-agent-port <port>
                          In-app DevFlow port for the test build (default: 9223)
  --apple-spike-timeout <seconds>
                           Host/device proof timeout (default: 180; maximum: 600)
  --verbosity <level>     quiet, minimal, normal, detailed, or diagnostic
  --verbose               Alias for --verbosity detailed
  --dry-run               Validate arguments and emit the planned, non-executing command as JSON
  --help                  Show this help text

Exit codes: 0 succeeded, 2 invalid invocation, 3 prerequisite/infrastructure failure,
4 flow failure, 5 pending capability or not-qualified result.
EOF
}

die_usage() {
  printf 'flow-qa: %s\n' "$1" >&2
  usage >&2
  exit "$EXIT_USAGE"
}

json_escape() {
  local value=${1-}
  value=${value//\\/\\\\}
  value=${value//\"/\\\"}
  value=${value//$'\n'/\\n}
  value=${value//$'\r'/\\r}
  value=${value//$'\t'/\\t}
  printf '%s' "$value"
}

json_string() {
  printf '"%s"' "$(json_escape "${1-}")"
}

json_array() {
  local first=1 item
  printf '['
  for item in "$@"; do
    if (( first == 0 )); then printf ','; fi
    json_string "$item"
    first=0
  done
  printf ']'
}

is_unsafe_path() {
  local path=$1
  [[ "$path" == *'*'* || "$path" == *'?'* || "$path" == *'['* || "$path" == *']'* ]] ||
    [[ "$path" =~ (^|[\\/])\.\.($|[\\/]) ]]
}

require_value() {
  local option=$1 value=${2-}
  [[ -n "$value" && "$value" != -* ]] || die_usage "$option requires a value."
}

validate_single_line() {
  local name=$1 value=$2
  [[ -n "$value" && "$value" != *$'\n'* && "$value" != *$'\r'* ]] ||
    die_usage "$name must be a single non-empty line."
}

assert_no_symlink() {
  local target=$1 relative current segment
  relative=${target#"$repo_root"/}
  [[ "$relative" != "$target" ]] || die_usage 'The output path must remain inside the repository.'
  current=$repo_root
  IFS='/' read -r -a components <<< "$relative"
  for segment in "${components[@]}"; do
    [[ -n "$segment" ]] || continue
    current="$current/$segment"
    [[ ! -L "$current" ]] || die_usage 'The output path must not traverse a symbolic link.'
  done
}

resolve_results_root() {
  local input=$1 expected candidate relative_input
  is_unsafe_path "$input" && die_usage '--results-root must not contain wildcards or parent-directory segments.'
  expected="$repo_root/artifacts/TestResults/devflow-flow/$platform"
  if [[ "$input" = /* ]]; then
    candidate=${input%/}
  else
    relative_input=$input
    while [[ "$relative_input" == ./* ]]; do
      relative_input=${relative_input#./}
    done
    candidate="$repo_root/${relative_input%/}"
  fi
  [[ "$candidate" == "$expected" ]] ||
    die_usage "--results-root must resolve exactly to '$expected' for platform '$platform'."
  assert_no_symlink "$candidate"
  resolved_results_root=$candidate
}

resolve_artifact_root() {
  local expected configured candidate relative_configured
  expected="$repo_root/artifacts/devflow/$run_id/$platform"
  configured=
  if [[ "$platform" == android ]]; then
    configured=${DEVFLOW_FLOW_PILOT_ARTIFACT_ROOT-}
  else
    configured=${DEVFLOW_FLOW_QA_ARTIFACT_ROOT-}
  fi
  if [[ -n "$configured" ]]; then
    is_unsafe_path "$configured" && die_usage 'Configured artifact root must not contain wildcards or parent-directory segments.'
    if [[ "$configured" = /* ]]; then
      candidate=${configured%/}
    else
      relative_configured=$configured
      while [[ "$relative_configured" == ./* ]]; do
        relative_configured=${relative_configured#./}
      done
      candidate="$repo_root/${relative_configured%/}"
    fi
    [[ "$candidate" == "$expected" ]] ||
      die_usage "The configured artifact root must resolve exactly to '$expected'."
  fi
  assert_no_symlink "$expected"
  resolved_artifact_root=$expected
}

repo_relative() {
  # Nothing outside the checkout may ever reach an artifact path. A consumer resolves these
  # entries against its own clone, so an absolute or escaping path is refused rather than
  # published as a machine-local location that cannot be verified.
  local path=$1 canonical
  canonical=$path
  while [[ "$canonical" == */./* ]]; do
    canonical=${canonical//\/.\//\/}
  done
  canonical=${canonical%/}
  if [[ "$canonical" == "$repo_root" ]]; then
    printf '.'
    return 0
  fi
  if [[ "$canonical" != "$repo_root"/* ]]; then
    return 1
  fi
  canonical=${canonical#"$repo_root"/}
  if [[ "$canonical" == /* || "$canonical" == ../* || "$canonical" == */../* || "$canonical" == *"/.." ]]; then
    return 1
  fi
  printf '%s' "$canonical"
}

json_string_or_null() {
  local value=${1-}
  if [[ -n "$value" ]]; then json_string "$value"; else printf 'null'; fi
}

sha256_file() {
  local path=$1
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$path" | awk '{print "sha256:" tolower($1)}'
  elif command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$path" | awk '{print "sha256:" tolower($1)}'
  else
    return 1
  fi
}

sha256_string() {
  local value=$1
  if command -v shasum >/dev/null 2>&1; then
    printf '%s' "$value" | shasum -a 256 | awk '{print "sha256:" tolower($1)}'
  elif command -v sha256sum >/dev/null 2>&1; then
    printf '%s' "$value" | sha256sum | awk '{print "sha256:" tolower($1)}'
  else
    return 1
  fi
}

atomic_write() {
  local target=$1 temporary
  mkdir -p -- "$(dirname -- "$target")"
  temporary="${target}.$$.tmp"
  temporary_paths+=("$temporary")
  umask 077
  cat > "$temporary"
  mv -f -- "$temporary" "$target"
}

cleanup_temporary_paths() {
  local path
  for path in "${temporary_paths[@]-}"; do
    [[ -n "$path" ]] && rm -f -- "$path"
  done
}

# Values this invocation was handed are held only in memory and are never written to an artifact.
# They are registered here so a diagnostic can be redacted by exact value: a signing identity,
# provisioning profile, or keychain reference frequently appears in tool output with no key,
# scheme, or assignment around it to key a pattern off.
secret_values=()

register_secret_value() {
  local value=${1-}
  value=${value#"${value%%[![:space:]]*}"}
  value=${value%"${value##*[![:space:]]}"}
  (( ${#value} >= 3 )) || return 0
  local existing
  for existing in ${secret_values[@]+"${secret_values[@]}"}; do
    [[ "$existing" == "$value" ]] && return 0
  done
  secret_values+=("$value")
  return 0
}

# Redaction runs on a stream, never on the terminal. Exact held values go first, because a value
# with no key or scheme around it is invisible to every pattern. A named credential then loses its
# value together with any authentication scheme in front of it. A bare scheme only loses what
# follows when that really looks like a credential: "digest", "basic", and "negotiate" are ordinary
# words in tool output, and "digest sha256:<hex>" is a diagnostic a reader needs.
redact_stream() {
  # Held values are exported inside this subshell, never passed on a command line: `env NAME=value`
  # would put the value in that process's argv, where the OS process table exposes it to any user
  # able to list processes.
  (
    local index=0 secret
    for secret in ${secret_values[@]+"${secret_values[@]}"}; do
      index=$((index + 1))
      export "FLOW_QA_SECRET_$index=$secret"
    done
    export FLOW_QA_SECRET_COUNT="$index"
    export FLOW_QA_MAX_LINES="$MAX_DIAGNOSTIC_LINES"
    awk '
    function literal_replace(text, needle, replacement,   out, position, lowerText, lowerNeedle, needleLength) {
      # Case-insensitive, exactly as the PowerShell rule is: a signing identity or keychain
      # reference is echoed back by tools with whatever casing they prefer, and a case-sensitive
      # match would publish the held value the moment one of them changed it.
      if (needle == "") return text
      lowerNeedle = tolower(needle)
      needleLength = length(needle)
      lowerText = tolower(text)
      out = ""
      while ((position = index(lowerText, lowerNeedle)) > 0) {
        out = out substr(text, 1, position - 1) replacement
        text = substr(text, position + needleLength)
        lowerText = substr(lowerText, position + needleLength)
      }
      return out text
    }
    function is_credential_token(token) {
      if (length(token) < 8) return 0
      if (token ~ /^[A-Za-z]+$/) return 0
      return token ~ /^[A-Za-z0-9+\/=_.-]+$/
    }
    # The credential-shaped run at the start of a word. A value that continues into other
    # punctuation - "<token>&next=2", "<token></header>" - is still a credential, so only the run
    # itself is replaced and the rest of the word is kept, exactly as the PowerShell rule does.
    function credential_prefix(token,   candidate) {
      if (match(token, /^[A-Za-z0-9+\/=_.-]+/)) {
        candidate = substr(token, RSTART, RLENGTH)
        if (is_credential_token(candidate)) return candidate
      }
      return ""
    }
    BEGIN {
      count = ENVIRON["FLOW_QA_SECRET_COUNT"] + 0
      maximumLines = ENVIRON["FLOW_QA_MAX_LINES"] + 0
      for (i = 1; i <= count; i++) secrets[i] = ENVIRON["FLOW_QA_SECRET_" i]
      split("bearer basic digest negotiate ntlm jwt", schemeList, " ")
      for (i in schemeList) schemes[schemeList[i]] = 1
      split("authorization proxy-authorization www-authenticate proxy-authenticate token password secret api_key api-key apikey devflow_ios_signing_identity devflow_ios_provisioning_profile devflow_ios_keychain devflow_apple_agent_session_secret", keyList, " ")
      for (i in keyList) keys[keyList[i]] = 1
      truncated = 0
    }
    {
      if (maximumLines > 0 && NR > maximumLines) { truncated = 1; next }
      line = $0
      # A trailing carriage return would otherwise defeat every token test on CRLF-terminated
      # output, leaving the credential in place.
      carriage = ""
      if (line ~ /\r$/) { carriage = "\r"; sub(/\r$/, "", line) }
      for (i = 1; i <= count; i++) line = literal_replace(line, secrets[i], "[REDACTED]")
      changed = (line carriage != $0)
      # Split on any run of blanks: a tab-separated credential is still a credential.
      wordCount = split(line, words, /[ \t]+/)
      pendingKey = 0
      pendingScheme = 0
      pendingSeparator = 0
      forcedValue = 0
      for (i = 1; i <= wordCount; i++) {
        word = words[i]
        if (word == "") continue
        lower = tolower(word)
        if (forcedValue) {
          words[i] = "[REDACTED]"
          changed = 1
          forcedValue = 0
          pendingKey = 0
          pendingScheme = 0
          pendingSeparator = 0
          continue
        }
        if (pendingSeparator) {
          pendingSeparator = 0
          if (word == ":" || word == "=") { pendingKey = 1; continue }
          if (match(word, /^[:=]/)) {
            rest = substr(word, 2)
            if (rest == "") { pendingKey = 1; continue }
            if (tolower(rest) in schemes) { forcedValue = 1; continue }
            words[i] = substr(word, 1, 1) "[REDACTED]"
            changed = 1
            continue
          }
        }
        if (pendingKey) {
          pendingKey = 0
          if (lower in schemes) { forcedValue = 1; continue }
          words[i] = "[REDACTED]"
          changed = 1
          continue
        }
        if (pendingScheme) {
          pendingScheme = 0
          prefix = credential_prefix(word)
          if (prefix != "") {
            words[i] = "[REDACTED]" substr(word, length(prefix) + 1)
            changed = 1
            continue
          }
        }
        if (match(lower, /^[a-z_][a-z0-9_.-]*[:=]/)) {
          name = substr(lower, 1, RLENGTH - 1)
          # A credential key is recognized as a whole word inside a longer header name, exactly as
          # the word boundary in the PowerShell rule does: "X-Api-Key:" and "X-Auth-Token:" are
          # keys, while "session_token=" is not, because underscore is a word character on both
          # sides.
          if (name in keys || name ~ /(^|[^a-z0-9_])(authorization|www-authenticate|proxy-authenticate|token|password|secret|api[_-]?key)$/) {
            rest = substr(word, RLENGTH + 1)
            if (rest == "") { pendingKey = 1; continue }
            if (tolower(rest) in schemes) { forcedValue = 1; continue }
            words[i] = substr(word, 1, RLENGTH) "[REDACTED]"
            changed = 1
            continue
          }
        }
        if (lower in keys || lower ~ /(^|[^a-z0-9_])(authorization|www-authenticate|proxy-authenticate|token|password|secret|api[_-]?key)$/) {
          pendingSeparator = 1
          continue
        }
        # The scheme may be glued to preceding markup - "<header>Bearer <token>" - which the
        # PowerShell rule matches through its word boundary.
        if (lower ~ /(^|[^a-z0-9_])(bearer|basic|digest|negotiate|ntlm|jwt)$/) { pendingScheme = 1 }
      }
      if (!changed) { print $0; next }
      rebuilt = words[1]
      for (i = 2; i <= wordCount; i++) rebuilt = rebuilt " " words[i]
      print rebuilt carriage
    }
    END {
      if (truncated) print "[truncated: the recorded diagnostic reached its line limit]"
    }
  '
  )
}

redact_diagnostic_file() {
  # The raw file is mode 0600, script-owned, and removed immediately after this bounded redacted
  # projection is written. Do not echo raw test output to the terminal.
  local source=$1 target=$2
  local full="${target}.redacted-$$.tmp"
  temporary_paths+=("$full")
  mkdir -p -- "$(dirname -- "$target")"
  # umask is not function-scoped, so the restriction is applied only around the writes it protects.
  ( umask 077; redact_stream <"$source" >"$full" )
  diagnostic_truncated=false
  if (( $(wc -c <"$full") > MAX_DIAGNOSTIC_BYTES )); then
    head -c "$MAX_DIAGNOSTIC_BYTES" "$full" >"$target"
    printf '\n[truncated: the recorded diagnostic reached its byte limit]\n' >>"$target"
    diagnostic_truncated=true
  else
    cat "$full" >"$target"
    if grep -q '\[truncated: the recorded diagnostic reached its line limit\]' "$target"; then
      diagnostic_truncated=true
    fi
  fi
  rm -f -- "$full"
}

# Structured run evidence is the only account of a failure that the platform host actually
# recorded. Free text that happens to contain "timeout" or "emulator" describes whatever the tool
# printed, which is why it may only answer when no structured evidence exists.
classify_structured_fields() {
  local outcome=${1-} failure_class=${2-}
  case "$failure_class" in
    capability-missing) printf 'capability-missing'; return 0 ;;
    infrastructure|transport|agent-disconnected|lease-conflict|lease-lost|reset-failed|timeout|secret-unavailable)
      printf 'infrastructure-failure'; return 0 ;;
  esac
  case "$outcome" in
    infrastructure-error|timed-out|lease-lost|orphaned|unknown-completion|cancelled)
      printf 'infrastructure-failure'; return 0 ;;
  esac
  if [[ "$outcome" == failed || -n "$failure_class" ]]; then
    printf 'flow-failure'
    return 0
  fi
  return 1
}

# A bounded scan of the structured evidence the host wrote next to this run. Only the fields the
# producers actually emit are read, and the script's own report is skipped so a previous pass
# cannot classify the next one.
structured_failure_classification() {
  [[ -d "$artifact_root" ]] || return 1
  local file joined values value observed=' ' classification
  local -a reports=()
  if [[ -f "$artifact_root/manifest.json" ]]; then
    reports+=("$artifact_root/manifest.json")
  fi
  while IFS= read -r -d '' file; do
    [[ "$file" == "$flow_run_path" ]] || reports+=("$file")
  done < <(find "$artifact_root" -type f -name 'flow-run.json' -print0 2>/dev/null | LC_ALL=C sort -z)

  for file in ${reports[@]+"${reports[@]}"}; do
    # `head` closing the pipe early makes `tr` fail under `pipefail`; that must not discard the
    # very evidence this scan exists to read.
    joined=$( { tr '\n\r\t' '   ' <"$file" 2>/dev/null || true; } | head -c 1048576 )
    [[ -n "$joined" ]] || continue
    grep -q '"kind"[[:space:]]*:[[:space:]]*"devflow-flow-qa-run"' <<<"$joined" && continue
    values=$(grep -Eo '"(failureClass|class)"[[:space:]]*:[[:space:]]*"[a-z-]+"' <<<"$joined" |
      sed -E 's/.*"([a-z-]+)"$/\1/' || true)
    for value in $values; do
      classification=$(classify_structured_fields '' "$value") && observed="$observed$classification "
    done
    values=$(grep -Eo '"(outcome)"[[:space:]]*:[[:space:]]*("[a-z-]+"|\{[^}]*"status"[[:space:]]*:[[:space:]]*"[a-z-]+")' <<<"$joined" |
      sed -E 's/.*"([a-z-]+)"$/\1/' || true)
    for value in $values; do
      classification=$(classify_structured_fields "$value" '') && observed="$observed$classification "
    done
  done

  if [[ "$observed" == *" capability-missing "* ]]; then
    printf 'capability-missing'
    return 0
  fi
  if [[ "$observed" == *" infrastructure-failure "* ]]; then
    printf 'infrastructure-failure'
    return 0
  fi
  if [[ "$observed" == *" flow-failure "* ]]; then
    printf 'flow-failure'
    return 0
  fi
  return 1
}

classify_execution() {
  # A missing or non-numeric exit status is never read as success: a test host that never launched
  # would otherwise turn into a passing lane.
  local exit_code=${1-} source=$2
  [[ "$exit_code" =~ ^[0-9]+$ ]] || exit_code=1
  if (( exit_code == 0 )); then
    printf 'passed'
  elif grep -Eiq '\bcapability-missing\b' "$source"; then
    printf 'capability-missing'
  elif grep -Eiq "$INFRASTRUCTURE_DIAGNOSTIC_PATTERN" "$source"; then
    printf 'infrastructure-failure'
  else
    printf 'flow-failure'
  fi
}

build_flow_digests_json() {
  local flow_dir="$flow_directory" file first=1 digest relative
  printf '['
  if [[ -d "$flow_dir" ]]; then
    while IFS= read -r -d '' file; do
      [[ "$(basename -- "$file")" == README.md ]] && continue
      relative=$(repo_relative "$file") || continue
      digest=$(sha256_file "$file" 2>/dev/null || true)
      if (( first == 0 )); then printf ','; fi
      printf '{"path":'
      json_string "$relative"
      printf ',"sha256":'
      if [[ -n "$digest" ]]; then json_string "$digest"; else printf 'null'; fi
      printf '}'
      first=0
    done < <(find "$flow_dir" -maxdepth 1 -type f -name '*.md' -print0 2>/dev/null | LC_ALL=C sort -z)
  fi
  printf ']'
}

# One artifact pass decides both reports. Discovery is sorted so two passes over the same tree
# produce the same list, every reference is repository-relative, and every file past the cap is
# counted rather than reported as a bare "truncated".
collect_artifacts() {
  artifact_records=()
  artifact_omissions=()
  artifact_omitted_by_limit=0
  artifact_omitted_outside_repository=0
  artifact_omitted_unhashable=0
  artifact_enumeration_errors=0
  local root file relative digest kind basename count=0 find_errors
  local -a seen=()
  # A directory this pass could not read is not an empty directory. `find` reports each one on
  # stderr; the count is kept and published so a partial inventory is never presented as a
  # complete one. The finalizer holds itself to the same rule.
  find_errors="$artifact_root/.flow-qa-find-$$.tmp"
  temporary_paths+=("$find_errors")
  for root in "$artifact_root" "$results_root"; do
    [[ -d "$root" ]] || continue
    while IFS= read -r -d '' file; do
      basename=$(basename -- "$file")
      [[ "$basename" == *.tmp ]] && continue
      # A file this same write pass rewrites after the digests are taken cannot be listed here.
      # Its recorded hash would describe bytes that no longer exist by the time the list is
      # published, and a consumer that checks the list would refuse the whole run.
      [[ "$file" == "$manifest_path" || "$file" == "$flow_run_path" ]] && continue
      if [[ "$root" == "$results_root" && "$basename" != *"$run_id"* ]]; then
        continue
      fi
      if ! relative=$(repo_relative "$file"); then
        artifact_omitted_outside_repository=$((artifact_omitted_outside_repository + 1))
        continue
      fi
      local duplicate=false existing
      for existing in ${seen[@]+"${seen[@]}"}; do
        [[ "$existing" == "$relative" ]] && duplicate=true && break
      done
      [[ "$duplicate" == true ]] && continue
      seen+=("$relative")
      if (( count >= MAX_ARTIFACTS )); then
        artifact_omitted_by_limit=$((artifact_omitted_by_limit + 1))
        continue
      fi
      digest=$(sha256_file "$file" 2>/dev/null || true)
      if [[ -z "$digest" ]]; then
        artifact_omitted_unhashable=$((artifact_omitted_unhashable + 1))
        artifact_omissions+=("{\"kind\":\"artifact-hash\",\"reason\":\"An artifact could not be hashed.\",\"path\":$(json_string "$relative")}")
        continue
      fi
      case "${file##*.}" in
        trx) kind=test-results ;;
        mauitrace) kind=mauitrace ;;
        json) kind=json ;;
        *) kind=host-diagnostic ;;
      esac
      artifact_records+=("{\"kind\":$(json_string "$kind"),\"path\":$(json_string "$relative"),\"sha256\":$(json_string "$digest"),\"sizeBytes\":$(wc -c < "$file" | tr -d ' '),\"redacted\":true}")
      count=$((count + 1))
    done < <(find "$root" -type f ! -name '*.tmp' -print0 2>>"$find_errors" | LC_ALL=C sort -z)
  done
  if [[ -s "$find_errors" ]]; then
    artifact_enumeration_errors=$(grep -c '' <"$find_errors" || printf '0')
  fi
  rm -f -- "$find_errors"
  # One omission for every excluded path, with a count, rather than the same sentence repeated
  # once per file.
  if (( artifact_omitted_outside_repository > 0 )); then
    artifact_omissions+=("{\"kind\":\"artifact-path\",\"reason\":\"An artifact outside the repository was excluded.\",\"omittedArtifacts\":$artifact_omitted_outside_repository}")
  fi
  if (( artifact_enumeration_errors > 0 )); then
    # A floor, not a measurement: an unreadable directory may have held any number of references.
    # Counting one keeps the summary and the omissions reconcilable.
    artifact_omissions+=("{\"kind\":\"artifact-enumeration\",\"reason\":\"An artifact directory could not be fully enumerated, so this inventory may be incomplete.\",\"omittedArtifacts\":$artifact_enumeration_errors,\"enumerationErrors\":$artifact_enumeration_errors}")
  fi
}

# The flow-run report is written in this same pass, so it is hashed after its final bytes exist and
# added to the list the manifest publishes. A report that cannot be hashed is reported, not
# silently dropped from a count that already claimed it.
append_flow_run_artifact() {
  local digest relative
  relative=$(repo_relative "$flow_run_path") || return 1
  digest=$(sha256_file "$flow_run_path" 2>/dev/null || true)
  if [[ -z "$digest" ]]; then
    write_omissions+=("{\"kind\":\"artifact-hash\",\"reason\":\"An artifact could not be hashed.\",\"path\":$(json_string "$relative")}")
    return 1
  fi
  artifact_records+=("{\"kind\":\"json\",\"path\":$(json_string "$relative"),\"sha256\":$(json_string "$digest"),\"sizeBytes\":$(wc -c < "$flow_run_path" | tr -d ' '),\"redacted\":true}")
  return 0
}

# A rewritten report has new bytes, so the digest recorded for it a moment ago describes a file
# that no longer exists. The stale record is dropped before the report is hashed again; leaving it
# would publish a hash a consumer cannot reproduce and lose it the whole run. The refresh only ever
# replaces a record that is already in the list: appending one here when the cap had excluded the
# report, or when it could not be hashed the first time, would publish one more artifact than the
# count both reports state.
refresh_flow_run_artifact() {
  local relative record found=false
  relative=$(repo_relative "$flow_run_path") || return 1
  local -a kept=()
  for record in ${artifact_records[@]+"${artifact_records[@]}"}; do
    if [[ "$record" == *"\"path\":$(json_string "$relative")"* ]]; then
      found=true
      continue
    fi
    kept+=("$record")
  done
  [[ "$found" == true ]] || return 1
  artifact_records=(${kept[@]+"${kept[@]}"})
  append_flow_run_artifact
}

# The unfinalized flow-pilot manifest is the only account the test process wrote of the attempts it
# observed. Overwriting it with the generic manifest destroyed that evidence outright, so it is
# copied to a fixed, bounded name first and published as an artifact of this run. Nothing about the
# copy is taken from input: the name is fixed, the source is the manifest path this pass owns, and
# a manifest too large to bound is reported rather than copied.
preserve_unfinalized_manifest() {
  preserved_manifest_relative=
  preserved_manifest_reason=
  preserved_manifest_record=
  preserved_manifest_replaced=0
  if [[ ! -f "$manifest_path" ]]; then
    preserved_manifest_reason='no-manifest'
    return 1
  fi

  local size target relative digest
  size=$(wc -c < "$manifest_path" | tr -d ' ')
  if [[ ! "$size" =~ ^[0-9]+$ ]] || (( size > MAX_PRESERVED_MANIFEST_BYTES )); then
    preserved_manifest_reason='manifest-too-large'
    return 1
  fi

  target="$(dirname -- "$manifest_path")/$UNFINALIZED_MANIFEST_NAME"
  # Scoped to the copy, exactly as atomic_write does: umask is process-wide, and leaving it at 077
  # would silently narrow the permissions of every file written after this point.
  ( umask 077; cp -f -- "$manifest_path" "$target" ) 2>/dev/null || {
    preserved_manifest_reason='copy-failed'
    return 1
  }

  relative=$(repo_relative "$target") || {
    rm -f -- "$target"
    preserved_manifest_reason='outside-repository'
    return 1
  }
  digest=$(sha256_file "$target" 2>/dev/null || true)
  if [[ -z "$digest" ]]; then
    # Removed rather than left behind: a file in the published directory that no artifact entry
    # accounts for is evidence a consumer cannot verify.
    rm -f -- "$target"
    preserved_manifest_reason='hash-failed'
    return 1
  fi

  # A previous run under the same run id may have left a copy that this pass's artifact scan
  # already hashed. Its digest describes bytes that were just overwritten, so the stale record is
  # dropped before the fresh one is added - two entries for one path, only one of which matches
  # the file, disqualifies the whole manifest.
  local record
  local -a kept=()
  for record in ${artifact_records[@]+"${artifact_records[@]}"}; do
    [[ "$record" == *"\"path\":$(json_string "$relative")"* ]] && continue
    kept+=("$record")
  done
  preserved_manifest_replaced=$(( ${#artifact_records[@]} - ${#kept[@]} ))
  artifact_records=(${kept[@]+"${kept[@]}"})

  preserved_manifest_relative=$relative
  preserved_manifest_record="{\"kind\":\"json\",\"path\":$(json_string "$relative"),\"sha256\":$(json_string "$digest"),\"sizeBytes\":$(wc -c < "$target" | tr -d ' '),\"redacted\":true}"
  return 0
}

# Both entry points publish the same list in the same order, so a consumer diffing two runs sees
# only what actually changed.
sort_artifact_records() {
  (( ${#artifact_records[@]} > 1 )) || return 0
  local record path line
  local -a sorted=()
  while IFS= read -r line; do
    [[ -n "$line" ]] || continue
    sorted+=("${line#*$'\t'}")
  done < <(
    for record in ${artifact_records[@]+"${artifact_records[@]}"}; do
      path=$(printf '%s' "$record" | sed -E 's/.*"path":"([^"]*)".*/\1/')
      printf '%s\t%s\n' "$path" "$record"
    done | LC_ALL=C sort -t "$(printf '\t')" -k1,1
  )
  artifact_records=(${sorted[@]+"${sorted[@]}"})
}

join_json_array() {
  local first=1 item
  printf '['
  for item in "$@"; do
    if (( first == 0 )); then printf ','; fi
    printf '%s' "$item"
    first=0
  done
  printf ']'
}

write_host_diagnostics() {
  atomic_write "$diagnostic_dir/summary.json" <<EOF
{
  "schema": 1,
  "kind": "devflow-flow-qa-host-diagnostics",
  "generatedAt": "$(date -u '+%Y-%m-%dT%H:%M:%SZ')",
  "status": $(json_string "$status"),
  "classification": $(json_string "$classification"),
  "host": {
    "hostOs": $(json_string "$host_os"),
    "dotnetSdk": $(json_string "$dotnet_sdk"),
    "workloadVersion": $(json_string "${DOTNET_WORKLOAD_VERSION-unknown}"),
    "xcode": $(json_string "$xcode_version"),
    "runtime": $(json_string "${ios_runtime:-default}"),
    "deviceEvidence": {
      "kind": $(json_string "$device_kind"),
      "realDevice": $physical_device,
      "deviceIdFingerprint": $device_id_json,
      "profile": "not-observed"
    },
    "signing": $signing_refs_json
  }
}
EOF
}

get_testing_package_version() {
  local report="$artifact_root/apple-xctest-spike.json" version=
  if [[ -f "$report" ]]; then
    version=$(sed -n 's/.*"assemblyVersion"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$report" | head -n 1)
  fi
  printf '%s' "${version:-unknown}"
}

write_flow_run() {
  local first_attempt=null app_digest app_digest_json=null testing_package_version app_relative
  app_digest=$(sha256_file "$app_project" 2>/dev/null || true)
  testing_package_version=$(get_testing_package_version)
  [[ -z "$app_digest" ]] || app_digest_json=$(json_string "$app_digest")
  app_relative=$(repo_relative "$app_project" || printf '')
  if (( ${#attempts[@]} > 0 )); then
    first_attempt=${attempts[0]}
  fi
  atomic_write "$flow_run_path" <<EOF
{
  "schema": 1,
  "kind": "devflow-flow-qa-run",
  "generatedAt": "$(date -u '+%Y-%m-%dT%H:%M:%SZ')",
  "repository": { "commit": $(json_string "$repository_commit") },
  "platform": $(json_string "$platform"),
  "experimental": $manifest_experimental,
  "backend": $manifest_backend,
  "officialCoverage": $manifest_official_coverage,
  "macCatalystEquivalent": $manifest_maccatalyst_equivalent,
  "app": {
    "project": $(json_string_or_null "$app_relative"),
    "sourceDigest": $app_digest_json,
    "packageDigest": null
  },
  "testing": { "packageVersion": $(json_string "$testing_package_version") },
  "flows": $(build_flow_digests_json),
  "appleQa": $apple_qa_json,
  "hostQa": {
    "runId": $(json_string "$run_id"),
    "configuration": $(json_string "$configuration"),
    "repeat": $repeat,
    "platformFilter": $(json_string "$base_filter"),
    "testFilterDigest": $(json_string "$test_filter_digest"),
    "noBuild": $no_build,
    "status": $(json_string "$status"),
    "classification": $(json_string "$classification"),
    "host": {
      "hostOs": $(json_string "$host_os"),
      "dotnetSdk": $(json_string "$dotnet_sdk"),
      "workloadVersion": $(json_string "${DOTNET_WORKLOAD_VERSION-unknown}"),
      "xcode": $(json_string "$xcode_version"),
      "runtime": $(json_string "${ios_runtime:-default}"),
      "deviceEvidence": {
        "kind": $(json_string "$device_kind"),
        "realDevice": $physical_device,
        "deviceIdFingerprint": $device_id_json,
        "profile": "not-observed"
      },
      "signing": $signing_refs_json
    },
    "resetSeed": {
      "resetFingerprint": $(json_string "$apple_reset_fingerprint"),
      "seedFingerprint": $(json_string "$apple_seed_fingerprint"),
      "backendStateFingerprint": $(json_string "$apple_backend_fingerprint")
    },
    "firstAttempt": $first_attempt,
    "cleanAttempts": $(join_json_array "${attempts[@]}"),
    "diagnosticReruns": [],
    "diagnosticRerunPolicy": "No automatic diagnostic rerun is performed because replay may mutate state.",
    "qualification": $qualification_json
    ,"appleSpike": $apple_spike_json
    ,"appleQa": $apple_qa_json
  },
  "firstAttempt": $first_attempt,
  "diagnosticReruns": [],
  "artifactSummary": $artifact_summary_json,
  "omissions": $(join_json_array ${write_omissions[@]+"${write_omissions[@]}"}),
  "privacy": {
    "excludedByDefault": ["screenshots", "source", "raw-model-context", "environment", "signing-inputs"]
  }
}
EOF
}

write_generic_manifest() {
  local app_digest app_digest_json=null testing_package_version app_relative
  app_digest=$(sha256_file "$app_project" 2>/dev/null || true)
  testing_package_version=$(get_testing_package_version)
  [[ -z "$app_digest" ]] || app_digest_json=$(json_string "$app_digest")
  app_relative=$(repo_relative "$app_project" || printf '')
  atomic_write "$manifest_path" <<EOF
{
  "schema": 1,
  "kind": "devflow-flow-qa",
  "generatedAt": "$(date -u '+%Y-%m-%dT%H:%M:%SZ')",
  "repository": { "commit": $(json_string "$repository_commit") },
  "workflow": { "runId": $(json_string "$run_id"), "attempt": $(json_string "${GITHUB_RUN_ATTEMPT-}") },
  "experimental": $manifest_experimental,
  "backend": $manifest_backend,
  "officialCoverage": $manifest_official_coverage,
  "macCatalystEquivalent": $manifest_maccatalyst_equivalent,
  "testing": {
    "project": "src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/Microsoft.Maui.DevFlow.Agent.IntegrationTests.csproj",
    "packageVersion": $(json_string "$testing_package_version")
  },
  "platform": {
    "name": $(json_string "$platform"),
    "host": {
      "hostOs": $(json_string "$host_os"),
      "dotnetSdk": $(json_string "$dotnet_sdk"),
      "workloadVersion": $(json_string "${DOTNET_WORKLOAD_VERSION-unknown}"),
      "xcode": $(json_string "$xcode_version"),
      "runtime": $(json_string "${ios_runtime:-default}"),
      "deviceEvidence": {
        "kind": $(json_string "$device_kind"),
        "realDevice": $physical_device,
        "deviceIdFingerprint": $device_id_json,
        "profile": "not-observed"
      },
      "signing": $signing_refs_json
    }
  },
  "app": {
    "project": $(json_string_or_null "$app_relative"),
    "sourceDigest": $app_digest_json,
    "packageDigest": null
  },
  "flows": $(build_flow_digests_json),
  "appleQa": $apple_qa_json,
  "hostQa": {
    "runId": $(json_string "$run_id"),
    "configuration": $(json_string "$configuration"),
    "repeat": $repeat,
    "platformFilter": $(json_string "$base_filter"),
    "testFilterDigest": $(json_string "$test_filter_digest"),
    "noBuild": $no_build,
    "status": $(json_string "$status"),
    "classification": $(json_string "$classification"),
    "firstAttempt": ${attempts[0]:-null},
    "cleanAttempts": $(join_json_array "${attempts[@]}"),
    "diagnosticReruns": [],
    "resetSeed": {
      "resetFingerprint": $(json_string "$apple_reset_fingerprint"),
      "seedFingerprint": $(json_string "$apple_seed_fingerprint"),
      "backendStateFingerprint": $(json_string "$apple_backend_fingerprint")
    },
    "qualification": $qualification_json
    ,"appleSpike": $apple_spike_json
    ,"appleQa": $apple_qa_json
  },
  "artifacts": $(join_json_array ${artifact_records[@]+"${artifact_records[@]}"}),
  "artifactSummary": $artifact_summary_json,
  "omissions": $(join_json_array ${write_omissions[@]+"${write_omissions[@]}"}),
  "privacy": {
    "excludedByDefault": ["screenshots", "source", "raw-model-context", "environment", "signing-inputs"]
  }
}
EOF
}

finalize_android_manifest() {
  local finalizer="$repo_root/eng/devflow/Finalize-DevFlowFlowPilotManifest.ps1" raw
  [[ -f "$finalizer" ]] || return 1
  command -v pwsh >/dev/null 2>&1 || return 1
  raw="$artifact_root/.flow-qa-finalize-$$.tmp"
  temporary_paths+=("$raw")
  umask 077
  FLOW_QA_FINALIZER="$finalizer" \
  FLOW_QA_MANIFEST_PATH="$manifest_path" \
  FLOW_QA_REPOSITORY_ROOT="$repo_root" \
  FLOW_QA_ARTIFACT_ROOT="$artifact_root" \
  FLOW_QA_RESULTS_ROOT="$results_root" \
  FLOW_QA_REPOSITORY_COMMIT="$repository_commit" \
  FLOW_QA_WORKFLOW_RUN_ID="$run_id" \
  FLOW_QA_ANDROID_API="${DEVFLOW_TEST_ANDROID_API-}" \
  FLOW_QA_ANDROID_AVD="${DEVFLOW_TEST_ANDROID_AVD-}" \
  pwsh -NoProfile -Command '$ErrorActionPreference = "Stop"; & $env:FLOW_QA_FINALIZER -ManifestPath $env:FLOW_QA_MANIFEST_PATH -RepositoryRoot $env:FLOW_QA_REPOSITORY_ROOT -ArtifactRoots @($env:FLOW_QA_ARTIFACT_ROOT, $env:FLOW_QA_RESULTS_ROOT) -Platform android -RepositoryCommit $env:FLOW_QA_REPOSITORY_COMMIT -WorkflowRunId $env:FLOW_QA_WORKFLOW_RUN_ID -AndroidApiLevel $env:FLOW_QA_ANDROID_API -AndroidAvdName $env:FLOW_QA_ANDROID_AVD -DeviceEvidenceKind emulator' >"$raw" 2>&1
  local code=$?
  rm -f -- "$raw"
  (( code == 0 )) || return "$code"
  # Success is confirmed from the artifact the finalizer must produce, never from a status some
  # earlier command left behind.
  [[ -f "$manifest_path" ]] || return 1
  grep -q '"finalizedAt"' "$manifest_path" || return 1
  return 0
}

# The four artifact facts both reports publish, stated once. `omittedArtifacts` counts every
# reference this pass excluded from the inventory, not only the ones the cap dropped: a summary
# that counted the cap alone reported a complete inventory for a run whose unhashable or
# out-of-repository evidence was missing from the list beside it. `truncated` stays the narrower
# fact it names - the cap, and only the cap, was reached.
set_artifact_summary() {
  local recorded=$1 by_limit=$2 other=$3 truncated=false
  (( by_limit > 0 )) && truncated=true
  artifact_summary_json="{\"maxArtifacts\":$MAX_ARTIFACTS,\"recordedArtifacts\":$recorded,\"omittedArtifacts\":$((by_limit + other)),\"truncated\":$truncated}"
}

# Every write pass derives its omissions again from what is observable at that moment. Appending
# them to the run-scoped list instead would publish a report whose omissions grow with the number
# of writes rather than with what was actually omitted.
write_artifacts() {
  write_omissions=(${omissions[@]+"${omissions[@]}"})
  if [[ "$classification" =~ ^(flow-failure|infrastructure-failure)$ ]] &&
    ! find "$artifact_root" -type f -name '*.mauitrace' -print -quit 2>/dev/null | grep -q .; then
    write_omissions+=('{"kind":"failure-evidence","reason":"No failure .mauitrace was available for this terminal outcome."}')
  fi
  if [[ "$platform" != android ]]; then
    write_omissions+=('{"kind":"package-digest","reason":"The platform host did not emit a packaged-app digest for this run."}')
  fi
  if [[ -z "$(sha256_file "$app_project" 2>/dev/null || true)" ]]; then
    write_omissions+=('{"kind":"app-digest","reason":"The selected app project was unavailable or could not be hashed."}')
  fi

  # One artifact pass decides both reports. Neither the manifest nor the flow-run report can appear
  # in a list taken before it exists, so every other artifact fact - what could not be hashed, how
  # many references the cap dropped, and how many were recorded - is computed once here and
  # published identically in both files. The host diagnostic is written first because it is final
  # for this pass and belongs in the list.
  write_host_diagnostics
  collect_artifacts
  write_omissions+=(${artifact_omissions[@]+"${artifact_omissions[@]}"})
  local flow_run_within_cap=true recorded_artifacts
  local omitted_by_limit=$artifact_omitted_by_limit
  local omitted_other=$((artifact_omitted_outside_repository + artifact_omitted_unhashable + artifact_enumeration_errors))
  if (( ${#artifact_records[@]} >= MAX_ARTIFACTS )); then
    flow_run_within_cap=false
    omitted_by_limit=$((omitted_by_limit + 1))
  fi
  recorded_artifacts=${#artifact_records[@]}
  if [[ "$flow_run_within_cap" == true ]]; then
    recorded_artifacts=$((recorded_artifacts + 1))
  fi
  if (( omitted_by_limit > 0 )); then
    write_omissions+=("{\"kind\":\"artifact-limit\",\"reason\":\"Only the first $MAX_ARTIFACTS artifact references were hashed.\",\"omittedArtifacts\":$omitted_by_limit}")
  fi
  set_artifact_summary "$recorded_artifacts" "$omitted_by_limit" "$omitted_other"

  write_flow_run
  if [[ "$flow_run_within_cap" == true ]]; then
    if ! append_flow_run_artifact; then
      # The report could not be hashed after all, so the count it published is wrong and both files
      # have to say so. The report carries no digest of itself, so correcting it in place is safe.
      flow_run_within_cap=false
      recorded_artifacts=${#artifact_records[@]}
      omitted_other=$((omitted_other + 1))
      set_artifact_summary "$recorded_artifacts" "$omitted_by_limit" "$omitted_other"
      write_flow_run
    fi
  fi
  sort_artifact_records
  if [[ "$platform" == android ]]; then
    if ! finalize_android_manifest; then
      # Whatever the failed finalization left behind is not a manifest this run can vouch for, but
      # it is the only account the test process wrote of the attempts it observed. It is copied to
      # a fixed bounded name and published as evidence before the generic manifest replaces it;
      # overwriting it outright destroyed the pilot evidence entirely. The same omission is stated
      # in both reports, because a flow-run that stayed silent about it would disagree with the
      # manifest a consumer reads beside it.
      if preserve_unfinalized_manifest; then
        # A stale copy the artifact scan had already listed was dropped by the preserve step, so
        # the recorded count follows it down before the fresh record is added.
        recorded_artifacts=$((recorded_artifacts - preserved_manifest_replaced))
        if (( recorded_artifacts >= MAX_ARTIFACTS )); then
          # Counted with the other exclusions rather than against the cap: the artifact-limit
          # omission has already been written with its own number, and adding to that number here
          # would leave the two disagreeing.
          omitted_other=$((omitted_other + 1))
          rm -f -- "$(dirname -- "$manifest_path")/$UNFINALIZED_MANIFEST_NAME"
          write_omissions+=("{\"kind\":\"shared-manifest\",\"reason\":\"The shared Android flow-pilot manifest could not be finalized, and the artifact cap left no room to preserve it.\",\"preserved\":false}")
        else
          artifact_records+=("$preserved_manifest_record")
          recorded_artifacts=$((recorded_artifacts + 1))
          write_omissions+=("{\"kind\":\"shared-manifest\",\"reason\":\"The shared Android flow-pilot manifest could not be finalized, so the unfinalized manifest was preserved beside the generic one.\",\"preserved\":true,\"preservedPath\":$(json_string "$preserved_manifest_relative")}")
        fi
      else
        write_omissions+=("{\"kind\":\"shared-manifest\",\"reason\":\"The shared Android flow-pilot manifest could not be finalized and the unfinalized manifest could not be preserved.\",\"preserved\":false,\"preservedFailure\":$(json_string "$preserved_manifest_reason")}")
      fi
      set_artifact_summary "$recorded_artifacts" "$omitted_by_limit" "$omitted_other"
      write_flow_run
      # Guarded on the record that is actually in the list: the report is hashed again only when a
      # digest for it was published, so a rewrite cannot add an artifact the counts never claimed.
      if [[ "$flow_run_within_cap" == true ]]; then
        if ! refresh_flow_run_artifact; then
          flow_run_within_cap=false
          recorded_artifacts=${#artifact_records[@]}
          omitted_other=$((omitted_other + 1))
          set_artifact_summary "$recorded_artifacts" "$omitted_by_limit" "$omitted_other"
          write_flow_run
        fi
      fi
      sort_artifact_records
      write_generic_manifest
    fi
  else
    write_generic_manifest
  fi
}

print_status() {
  printf 'flow-qa: platform=%s status=%s classification=%s artifacts=%s\n' \
    "$platform" "$status" "$classification" "$(repo_relative "$artifact_root" || printf 'unavailable')" >&2
}

run_test_command() {
  # Split deliberately. `local` is a builtin, so every word on the line is expanded before any
  # assignment takes effect; writing `local attempt=$1 raw="...$attempt..."` expands $attempt while
  # it is still unset and aborts under `set -u` with "attempt: unbound variable".
  local attempt=$1
  local raw="$artifact_root/.flow-qa-command-$attempt-$$.tmp"
  local diagnostic="$diagnostic_dir/test-output-attempt-$attempt.txt"
  local -a attempt_arguments=("${test_arguments[@]}")
  local index
  for index in "${!attempt_arguments[@]}"; do
    attempt_arguments[$index]=${attempt_arguments[$index]//\{attempt\}/$attempt}
  done
  temporary_paths+=("$raw")
  umask 077
  # test_arguments starts at the `test` subcommand, so the driver has to be named here, exactly as
  # run_qualification does. Without it the shell ran its own `test` builtin, which reported
  # "syntax error: `--configuration' unexpected" and looked like a malformed flow rather than a
  # missing driver.
  dotnet "${attempt_arguments[@]}" >"$raw" 2>&1
  command_exit=$?
  # A missing or non-numeric status is never read as success. A test host that never launched would
  # otherwise turn into a passing lane.
  [[ "$command_exit" =~ ^[0-9]+$ ]] || command_exit=1
  redact_diagnostic_file "$raw" "$diagnostic"
  command_truncated=$diagnostic_truncated
  command_output=$(head -c "$MAX_DIAGNOSTIC_BYTES" "$diagnostic" || true)
  if (( command_exit == 0 )); then
    command_classification=passed
    command_classification_source=exit-code
  else
    local structured
    if structured=$(structured_failure_classification); then
      command_classification=$structured
      command_classification_source=structured-evidence
    else
      # Classified from the full captured output, not from the bounded projection: a
      # capability-missing marker past the truncation point would otherwise be read as a flow
      # failure. The raw file is script-owned and deleted below; only the redacted file is
      # published.
      command_classification=$(classify_execution "$command_exit" "$raw")
      command_classification_source=diagnostic-text
    fi
  fi
  rm -f -- "$raw"
}

attempt_json() {
  local kind=$1 repetition=$2 extra=${3-}
  printf '{"kind":%s,"repetition":%s%s,"exitCode":%s,"classification":%s,"classificationSource":%s,"diagnosticTruncated":%s,"diagnostic":%s}' \
    "$(json_string "$kind")" \
    "$repetition" \
    "$extra" \
    "$command_exit" \
    "$(json_string "$command_classification")" \
    "$(json_string "$command_classification_source")" \
    "$command_truncated" \
    "$(json_string_or_null "$(repo_relative "$diagnostic_dir/test-output-attempt-$repetition.txt" || printf '')")"
}

run_qualification() {
  local cli_project="$repo_root/src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj"
  local output="$artifact_root/qualification.json"
  local raw="$artifact_root/.flow-qa-qualification-$$.tmp"
  local -a arguments=(run --project "$cli_project" -f net10.0 --configuration "$configuration")
  [[ "$no_build" == true ]] && arguments+=(--no-build)
  arguments+=(-- devflow flow qualify --platform "$platform" --corpus "$repo_root/tests/DevFlow/InspectorCorpus" --artifact-manifest "$manifest_path" --output "$output" --json --fail-on-non-pass)
  [[ -n "$accumulate_directory" ]] && arguments+=(--accumulate "$accumulate_directory")
  [[ -n "$baseline_path" ]] && arguments+=(--baseline "$baseline_path")
  temporary_paths+=("$raw")
  umask 077
  dotnet "${arguments[@]}" >"$raw" 2>&1
  local code=$?
  redact_diagnostic_file "$raw" "$diagnostic_dir/qualification-output.txt"
  rm -f -- "$raw"
  if (( code == 0 )); then
    qualification_json='{"status":"qualified"}'
    return 0
  fi
  if [[ -f "$output" ]] && grep -Eq '"status"[[:space:]]*:[[:space:]]*"not-qualified"' "$output"; then
    qualification_json='{"status":"not-qualified"}'
    return "$EXIT_PENDING"
  fi
  qualification_json='{"status":"qualification-failed"}'
  return "$EXIT_PREREQUISITE"
}

apple_info_plist() {
  if [[ "$platform" == ios ]]; then
    printf '%s' "$apple_target_app/Info.plist"
  else
    printf '%s' "$apple_target_app/Contents/Info.plist"
  fi
}

select_ios_simulator() {
  [[ -n "$simulator_id" ]] && return 0
  local json="$artifact_root/.apple-simulators-$$.json"
  temporary_paths+=("$json")
  if ! xcrun simctl list devices available --json >"$json" 2>/dev/null; then
    return 1
  fi
  simulator_id=$(python3 - "$json" "${ios_runtime:-}" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    devices = json.load(source).get("devices", {})
selector = sys.argv[2].lower().replace("x", "").rstrip(".")
candidates = []
for runtime, values in devices.items():
    if "iOS" not in runtime:
        continue
    if selector and selector not in runtime.lower().replace("-", "."):
        continue
    for value in values:
        if value.get("isAvailable", True) and "iPhone" in value.get("name", ""):
            candidates.append((value.get("state") == "Booted", value.get("udid", "")))
for _, udid in sorted(candidates, reverse=True):
    if udid:
        print(udid)
        break
PY
) || return 1
  [[ -n "$simulator_id" ]]
}

collect_ios_simulator_metadata() {
  local json="$artifact_root/.apple-simulator-metadata-$$.json" metadata
  temporary_paths+=("$json")
  xcrun simctl list devices --json >"$json" 2>/dev/null || return 0
  metadata=$(python3 - "$json" "$simulator_id" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    devices = json.load(source).get("devices", {})
for runtime, values in devices.items():
    for value in values:
        if value.get("udid") == sys.argv[2]:
            print(runtime.replace("com.apple.CoreSimulator.SimRuntime.", "").replace("-", "."))
            print(value.get("name", "unknown"))
            raise SystemExit(0)
PY
)
  local lines=()
  while IFS= read -r line; do lines+=("$line"); done <<< "$metadata"
  [[ -n "${lines[0]-}" ]] && apple_simulator_runtime=${lines[0]}
  [[ -n "${lines[1]-}" ]] && apple_simulator_profile=${lines[1]}
}

build_apple_target() {
  local raw="$artifact_root/.apple-target-build-$$.tmp" runtime_id search_root found project_name
  if [[ -n "$apple_target_app" && -d "$apple_target_app" ]]; then
    return 0
  fi
  if [[ -n "$target_app" && ! -d "$apple_target_app" ]]; then
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-target-app","reason":"The supplied --target-app directory was not found."}')
    return "$EXIT_PENDING"
  fi
  temporary_paths+=("$raw")
  local -a build_arguments=(build "$app_project" -f "net10.0-$platform" -c "$configuration" --nologo -v minimal
    -p:DevFlowIntegrationTest=true -p:MauiDevFlowPort="$apple_in_app_agent_port")
  if [[ "$platform" == ios ]]; then
    if [[ "$(uname -m)" == arm64 ]]; then runtime_id=iossimulator-arm64; else runtime_id=iossimulator-x64; fi
    build_arguments+=(-p:_DeviceTarget=simulator -p:RuntimeIdentifier="$runtime_id")
  fi
  if ! dotnet "${build_arguments[@]}" >"$raw" 2>&1; then
    redact_diagnostic_file "$raw" "$diagnostic_dir/apple-target-build.txt"
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-target-build","reason":"The instrumented DevFlow.Sample Apple target could not be built on this host."}')
    return "$EXIT_PENDING"
  fi
  redact_diagnostic_file "$raw" "$diagnostic_dir/apple-target-build.txt"
  rm -f -- "$raw"
  project_name=$(basename -- "$app_project" .csproj)
  search_root="$repo_root/artifacts/bin/$project_name/$configuration/net10.0-$platform"
  if [[ ! -d "$search_root" ]]; then
    search_root="$(dirname -- "$app_project")/bin/$configuration/net10.0-$platform"
  fi
  found=$(find "$search_root" -type d -name '*.app' -print -quit 2>/dev/null || true)
  if [[ -z "$found" ]]; then
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-target-build","reason":"The Apple build completed without a discoverable .app bundle."}')
    return "$EXIT_PENDING"
  fi
  apple_target_app=$found
  return 0
}

verify_apple_target_identity() {
  local plist observed
  plist=$(apple_info_plist)
  [[ -f "$plist" ]] || return 1
  observed=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$plist" 2>/dev/null || true)
  [[ -n "$observed" && "$observed" == "$target_bundle_id" ]]
}

prepare_apple_target() {
  if ! command -v xcodebuild >/dev/null 2>&1 || ! command -v xcrun >/dev/null 2>&1 ||
    ! command -v python3 >/dev/null 2>&1; then
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-xctest-toolchain","reason":"xcodebuild, xcrun, and python3 are required for Apple flow QA; the script does not install them."}')
    return "$EXIT_PENDING"
  fi
  build_apple_target
  local build_exit=$?
  if (( build_exit != 0 )); then
    return "$build_exit"
  fi
  if ! verify_apple_target_identity; then
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-target-identity","reason":"The instrumented target bundle identifier did not match --target-bundle-id."}')
    return "$EXIT_PENDING"
  fi
  if [[ "$platform" == ios ]] && ! select_ios_simulator; then
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-simulator","reason":"No available iPhone simulator could be selected."}')
    return "$EXIT_PENDING"
  fi
  if [[ "$platform" == ios ]]; then
    xcrun simctl boot "$simulator_id" >/dev/null 2>&1 || true
    if ! xcrun simctl bootstatus "$simulator_id" -b >/dev/null 2>&1; then
      status=pending-spike
      classification=capability-missing
      omissions+=('{"kind":"apple-simulator","reason":"The selected iOS Simulator did not become booted."}')
      return "$EXIT_PENDING"
    fi
    collect_ios_simulator_metadata
  fi
  return 0
}

# The XCTest agent authenticates to this endpoint with the ephemeral secret this script minted, so
# where the endpoint points decides who receives that secret. The endpoint is read out of a
# readiness file, which is an artifact on disk: a stale, racing, or tampered one naming a remote
# host would hand the credential to whatever answers there. Only a loopback endpoint is accepted,
# and it is checked before the endpoint is exported to any process that carries the secret.
is_loopback_endpoint() {
  local endpoint=${1-} authority host
  [[ "$endpoint" =~ ^(http|https):// ]] || return 1
  authority=${endpoint#*://}
  authority=${authority%%/*}
  # Userinfo is refused outright: "127.0.0.1@evil.example" reads as loopback to a careless test.
  [[ "$authority" == *@* ]] && return 1
  host=$authority
  if [[ "$host" == \[*\]* ]]; then
    host=${host#\[}
    host=${host%%\]*}
  else
    host=${host%%:*}
  fi
  [[ -n "$host" ]] || return 1
  case "$host" in
    localhost) return 0 ;;
    ::1 | 0:0:0:0:0:0:0:1) return 0 ;;
    127.*)
      [[ "$host" =~ ^127\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$ ]] && return 0
      return 1
      ;;
  esac
  return 1
}

reset_apple_target_for_flow() {
  local raw="$artifact_root/.apple-reset-$$.tmp"
  temporary_paths+=("$raw")
  if [[ "$platform" == ios ]]; then
    xcrun simctl terminate "$simulator_id" "$target_bundle_id" >"$raw" 2>&1 || true
    if ! xcrun simctl uninstall "$simulator_id" "$target_bundle_id" >>"$raw" 2>&1; then
      if ! grep -Eqi 'not installed|not found' "$raw"; then
        redact_diagnostic_file "$raw" "$diagnostic_dir/apple-reset.txt"
        return 1
      fi
    fi
    if ! xcrun simctl install "$simulator_id" "$apple_target_app" >>"$raw" 2>&1; then
      redact_diagnostic_file "$raw" "$diagnostic_dir/apple-reset.txt"
      return 1
    fi
  else
    # This is a process-only safe reset. The target is compiled with the explicit
    # DEVFLOW_INTEGRATION_TEST seed hook; no user container, Keychain, or AppKit lane is erased.
    osascript -e "tell application id \"$target_bundle_id\" to quit" >"$raw" 2>&1 || true
    if ! open -g -n "$apple_target_app" >>"$raw" 2>&1; then
      redact_diagnostic_file "$raw" "$diagnostic_dir/apple-reset.txt"
      return 1
    fi
  fi
  redact_diagnostic_file "$raw" "$diagnostic_dir/apple-reset.txt"
  rm -f -- "$raw"
  return 0
}

run_apple_flow_attempt() {
  local flow_path=$1 repetition=$2 flow_name session_root host_ready host_raw xcode_raw
  local host_pid= host_exit=1 xcode_exit=1 endpoint= session_secret= target_digest=
  flow_name=$(basename -- "$flow_path" .md)
  session_root="$artifact_root/apple-flow-runs/$flow_name/attempt-$repetition"
  host_ready="$session_root/apple-xctest-host-ready.json"
  host_raw="$session_root/.apple-host-$$.tmp"
  xcode_raw="$session_root/.apple-xcode-$$.tmp"
  mkdir -p -- "$session_root"

  if ! reset_apple_target_for_flow; then
    apple_flow_attempts+=("{\"flow\":$(json_string "$flow_name"),\"repetition\":$repetition,\"status\":\"infrastructure-error\",\"reason\":\"reset\"}")
    return "$EXIT_PREREQUISITE"
  fi
  session_secret=$(openssl rand -hex 32) || return "$EXIT_PREREQUISITE"
  # Registered where it is minted. A bare hex secret carries no key or scheme, so only exact-value
  # redaction can keep it out of a published diagnostic.
  register_secret_value "$session_secret"
  target_digest=$(sha256_file "$(apple_info_plist)" 2>/dev/null || true)
  export DEVFLOW_APPLE_AGENT_SESSION_SECRET="$session_secret"
  export DEVFLOW_APPLE_AGENT_SESSION_ID="apple-$run_id-$flow_name-$repetition"
  export DEVFLOW_TARGET_BUNDLE_ID="$target_bundle_id"
  export DEVFLOW_APPLE_AGENT_PLATFORM="$platform"
  export DEVFLOW_APPLE_AGENT_TIMEOUT_SECONDS="$apple_spike_timeout"
  export DEVFLOW_APPLE_IN_APP_AGENT_PORT="$apple_in_app_agent_port"
  export DEVFLOW_APPLE_TEST_SEED=devflow-sample-v1
  [[ -z "$target_digest" ]] || export DEVFLOW_TARGET_APP_DIGEST="$target_digest"

  local -a host_arguments=(run --project "$apple_agent_host_project" --configuration "$configuration")
  [[ "$no_build" == true ]] && host_arguments+=(--no-build)
  host_arguments+=(-- --mode flow --session-id "$DEVFLOW_APPLE_AGENT_SESSION_ID" --platform "$platform"
    --target-bundle-id "$target_bundle_id" --artifact-root "$session_root" --ready-file "$host_ready"
    --safe-action-id "$safe_action_id" --flow-file "$flow_path" --run-id "$flow_name-attempt-$repetition"
    --timeout-seconds "$apple_spike_timeout")
  [[ -z "$target_digest" ]] || host_arguments+=(--target-app-digest "$target_digest")
  local -a xcode_arguments=(test -project "$apple_agent_native_project" -scheme DevFlowAppleTestAgent)
  if [[ "$platform" == ios ]]; then
    xcode_arguments+=(-destination "platform=iOS Simulator,id=$simulator_id")
  else
    xcode_arguments+=(-destination 'platform=macOS')
  fi

  temporary_paths+=("$host_raw" "$xcode_raw")
  umask 077
  dotnet "${host_arguments[@]}" >"$host_raw" 2>&1 &
  host_pid=$!
  local ready_attempt
  for (( ready_attempt = 0; ready_attempt < 30; ready_attempt++ )); do
    [[ -f "$host_ready" ]] && break
    kill -0 "$host_pid" 2>/dev/null || break
    sleep 1
  done
  if [[ ! -f "$host_ready" ]]; then
    wait "$host_pid"; host_exit=$?
    redact_diagnostic_file "$host_raw" "$diagnostic_dir/apple-flow-host-$flow_name-$repetition.txt"
    apple_flow_attempts+=("{\"flow\":$(json_string "$flow_name"),\"repetition\":$repetition,\"status\":\"infrastructure-error\",\"reason\":\"host-ready\"}")
    unset DEVFLOW_APPLE_AGENT_SESSION_SECRET
    return "$EXIT_PREREQUISITE"
  fi
  endpoint=$(sed -n 's/.*"endpoint"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$host_ready" | head -n 1)
  if [[ -z "$endpoint" ]]; then
    kill -TERM "$host_pid" 2>/dev/null || true
    wait "$host_pid" || true
    redact_diagnostic_file "$host_raw" "$diagnostic_dir/apple-flow-host-$flow_name-$repetition.txt"
    apple_flow_attempts+=("{\"flow\":$(json_string "$flow_name"),\"repetition\":$repetition,\"status\":\"infrastructure-error\",\"reason\":\"endpoint\"}")
    unset DEVFLOW_APPLE_AGENT_SESSION_SECRET
    return "$EXIT_PREREQUISITE"
  fi
  if ! is_loopback_endpoint "$endpoint"; then
    kill -TERM "$host_pid" 2>/dev/null || true
    wait "$host_pid" || true
    redact_diagnostic_file "$host_raw" "$diagnostic_dir/apple-flow-host-$flow_name-$repetition.txt"
    omissions+=('{"kind":"apple-transport-endpoint","reason":"The host readiness report named a non-loopback endpoint, so the minted session secret was withheld."}')
    apple_flow_attempts+=("{\"flow\":$(json_string "$flow_name"),\"repetition\":$repetition,\"status\":\"infrastructure-error\",\"reason\":\"endpoint-not-loopback\"}")
    unset DEVFLOW_APPLE_AGENT_SESSION_SECRET
    return "$EXIT_PREREQUISITE"
  fi
  export DEVFLOW_APPLE_AGENT_ENDPOINT="$endpoint"
  # Same defect as run_test_command: the array starts at the `test` subcommand, so omitting the
  # driver runs the shell's own `test` builtin against xcodebuild's flags. These lanes carry
  # continue-on-error, so it degraded silently instead of failing the run.
  xcodebuild "${xcode_arguments[@]}" >"$xcode_raw" 2>&1
  xcode_exit=$?
  wait "$host_pid"; host_exit=$?
  redact_diagnostic_file "$host_raw" "$diagnostic_dir/apple-flow-host-$flow_name-$repetition.txt"
  redact_diagnostic_file "$xcode_raw" "$diagnostic_dir/apple-flow-xcode-$flow_name-$repetition.txt"
  rm -f -- "$host_raw" "$xcode_raw"
  unset DEVFLOW_APPLE_AGENT_SESSION_SECRET DEVFLOW_APPLE_AGENT_ENDPOINT DEVFLOW_TARGET_APP_DIGEST

  local run_report="$session_root/apple-test-agent-run.json" flow_report="$session_root/flow-runs/$flow_name-attempt-$repetition/flow-run.json"
  local outcome=failed report_digest=
  if [[ -f "$run_report" ]]; then
    outcome=$(sed -n 's/.*"status"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$run_report" | head -n 1)
  fi
  if [[ -f "$flow_report" ]]; then
    report_digest=$(sed -n 's/.*"reportDigest"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$flow_report" | head -n 1)
  fi
  if [[ "$host_exit" == 0 && "$xcode_exit" == 0 && "$outcome" == passed ]]; then
    apple_flow_attempts+=("{\"flow\":$(json_string "$flow_name"),\"repetition\":$repetition,\"status\":\"passed\",\"report\":$(json_string "$(repo_relative "$flow_report")"),\"reportDigest\":$(json_string "$report_digest")}")
    return "$EXIT_SUCCESS"
  fi
  apple_flow_attempts+=("{\"flow\":$(json_string "$flow_name"),\"repetition\":$repetition,\"status\":$(json_string "${outcome:-failed}"),\"report\":$(json_string "$(repo_relative "$flow_report")")}")
  return "$EXIT_FLOW_FAILURE"
}

write_apple_qa_manifest() {
  local output="$artifact_root/apple-flow-qa.json" flow_path flow_name first=1 flow_entries=()
  local spike_path="$artifact_root/apple-xctest-spike.json" spike_status=not-proved foreground=false authenticated=false receipt=false cancellation=false parity=false
  local testing_version=unknown simulator_fingerprint=null checkpoint_report= webview_outcome=unsupported-by-xcuitest-agent
  [[ -f "$spike_path" ]] && spike_status=$(sed -n 's/.*"status"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$spike_path" | head -n 1)
  [[ -f "$spike_path" ]] && grep -Eq '"asserted"[[:space:]]*:[[:space:]]*true' "$spike_path" && foreground=true
  [[ -f "$spike_path" ]] && grep -Eq '"authenticated"[[:space:]]*:[[:space:]]*true' "$spike_path" && authenticated=true
  [[ -f "$spike_path" ]] && grep -Eq '"commandReceipt"[[:space:]]*:[[:space:]]*\{' "$spike_path" && receipt=true
  [[ -f "$spike_path" ]] && grep -Eq '"code"[[:space:]]*:[[:space:]]*"apple-agent-cancelled"' "$spike_path" && cancellation=true
  [[ -f "$spike_path" ]] && grep -Eq '"passed"[[:space:]]*:[[:space:]]*true' "$spike_path" && parity=true
  [[ -f "$spike_path" ]] && testing_version=$(sed -n 's/.*"assemblyVersion"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$spike_path" | head -n 1)
  [[ -n "$testing_version" ]] || testing_version=unknown
  [[ "$platform" != macos ]] || webview_outcome=capability-gated-by-appkit-fixture
  if [[ "$platform" == ios && -n "$simulator_id" ]]; then
    simulator_fingerprint=$(json_string "$(sha256_string "$simulator_id")")
  fi
  checkpoint_report=$(find "$artifact_root/apple-flow-runs" -type f -name flow-run.json -print -quit 2>/dev/null || true)
  if [[ -n "$checkpoint_report" ]]; then
    apple_reset_fingerprint=$(sed -n 's/.*"resetIdentity"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$checkpoint_report" | head -n 1)
    apple_seed_fingerprint=$(sed -n 's/.*"seedFingerprint"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$checkpoint_report" | head -n 1)
    apple_backend_fingerprint=$(sed -n 's/.*"backendStateFingerprint"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$checkpoint_report" | head -n 1)
    apple_reset_fingerprint=${apple_reset_fingerprint:-not-observed}
    apple_seed_fingerprint=${apple_seed_fingerprint:-not-observed}
    apple_backend_fingerprint=${apple_backend_fingerprint:-not-observed}
  fi
  shopt -s nullglob
  for flow_path in "$flow_directory"/*.md; do
    [[ "$(basename -- "$flow_path")" == README.md ]] && continue
    flow_name=$(basename -- "$flow_path" .md)
    local attempts=() record
    for record in "${apple_flow_attempts[@]}"; do
      [[ "$record" == *"\"flow\":\"$flow_name\""* ]] && attempts+=("$record")
    done
    local first_attempt=null
    (( ${#attempts[@]} > 0 )) && first_attempt=${attempts[0]}
    flow_entries+=("{\"name\":$(json_string "$flow_name"),\"firstAttempt\":$first_attempt,\"cleanAttempts\":$(join_json_array "${attempts[@]}")}")
  done
  shopt -u nullglob
  local signing_json='{"physical":false,"identityRef":null,"provisioningProfileRef":null,"keychainRef":null}'
  if [[ "$physical_device" == true ]]; then
    signing_json="{\"physical\":true,\"identityRef\":$([[ -n "$signing_identity" ]] && json_string "$(sha256_string "$signing_identity")" || printf null),\"provisioningProfileRef\":$([[ -n "$provisioning_profile" ]] && json_string "$(sha256_string "$provisioning_profile")" || printf null),\"keychainRef\":$([[ -n "$keychain" ]] && json_string "$(sha256_string "$keychain")" || printf null)}"
  fi
  atomic_write "$output" <<EOF
{
  "schema": 1,
  "kind": "devflow-apple-flow-qa",
  "generatedAt": "$(date -u '+%Y-%m-%dT%H:%M:%SZ')",
  "platform": $(json_string "$platform"),
  "experimental": $manifest_experimental,
  "backend": $manifest_backend,
  "officialCoverage": $manifest_official_coverage,
  "macCatalystEquivalent": $manifest_maccatalyst_equivalent,
  "spike": {
    "status": $(json_string "$spike_status"),
    "foregroundProof": $foreground,
    "authenticatedTransport": $authenticated,
    "receipt": $receipt,
    "cancellation": $cancellation,
    "parity": $parity
  },
  "checkpoint": {
    "resetFingerprint": $(json_string "$apple_reset_fingerprint"),
    "seedFingerprint": $(json_string "$apple_seed_fingerprint"),
    "backendStateFingerprint": $(json_string "$apple_backend_fingerprint")
  },
  "apple": {
    "xcodeVersion": $(json_string "$xcode_version"),
    "simulatorRuntime": $(json_string "$apple_simulator_runtime"),
    "simulatorDeviceFingerprint": $simulator_fingerprint,
    "simulatorDeviceProfile": $(json_string "$apple_simulator_profile"),
    "signing": $signing_json,
    "testAgent": { "hostProject": "Microsoft.Maui.DevFlow.TestAgent.Host", "nativeVersion": "1.0.0-experimental" },
    "testingPackageVersion": $(json_string "$testing_version"),
    "artifactTrust": "sha256-bound-redacted-manifest"
  },
  "flows": $(join_json_array "${flow_entries[@]}"),
  "contractOutcomes": {
    "agentAuthoredFlow": "covered-by-reviewed-flow",
    "selectorIdentity": "covered-by-runtime-duplicate-rejection",
    "shellModalRoute": "covered-by-modal-roundtrip",
    "webViewContext": $(json_string "$webview_outcome"),
    "repairAbstention": "covered-by-static-contract",
    "sourceProposal": "covered-by-static-contract",
    "securityPrivacy": "covered-by-static-contract",
    "reportParity": "covered-by-normalized-runner-report"
  }
}
EOF
  apple_qa_json=$(cat "$output")
}

run_apple_flow_qa() {
  local flow_path repetition code=0 any_failure=false
  apple_flow_attempts=()
  for flow_path in "$flow_directory"/*.md; do
    [[ "$(basename -- "$flow_path")" == README.md ]] && continue
    for (( repetition = 1; repetition <= repeat; repetition++ )); do
      if ! run_apple_flow_attempt "$flow_path" "$repetition"; then
        any_failure=true
      fi
    done
  done
  write_apple_qa_manifest
  [[ "$any_failure" == false ]]
}

run_apple_spike() {
  local host_ready="$artifact_root/apple-xctest-host-ready.json"
  local host_raw="$artifact_root/.apple-xctest-host-$$.tmp"
  local xcode_raw="$artifact_root/.apple-xctest-xcode-$$.tmp"
  local host_pid= host_exit=1 xcode_exit=1 endpoint= session_secret= target_digest=
  local -a host_arguments=(run --project "$apple_agent_host_project" --configuration "$configuration")
  local -a xcode_arguments=(test -project "$apple_agent_native_project" -scheme DevFlowAppleTestAgent)

  if [[ ! -f "$apple_agent_host_project" || ! -f "$apple_agent_native_project/project.pbxproj" || ! -f "$apple_agent_swift_source" ]]; then
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-xctest-agent","reason":"The checked-in Apple XCTest host or native source is unavailable."}')
    return "$EXIT_PENDING"
  fi
  prepare_apple_target || return "$?"
  if ! command -v openssl >/dev/null 2>&1; then
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-transport-auth","reason":"openssl is required to generate the ephemeral HMAC session secret; no secret fallback is used."}')
    return "$EXIT_PENDING"
  fi

  session_secret=$(openssl rand -hex 32) || {
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-transport-auth","reason":"The ephemeral HMAC session secret could not be generated."}')
    return "$EXIT_PENDING"
  }
  # Registered where it is minted. A bare hex secret carries no key or scheme, so only exact-value
  # redaction can keep it out of a published diagnostic.
  register_secret_value "$session_secret"
  export DEVFLOW_APPLE_AGENT_SESSION_SECRET="$session_secret"
  export DEVFLOW_APPLE_AGENT_SESSION_ID="apple-$run_id"
  export DEVFLOW_TARGET_BUNDLE_ID="$target_bundle_id"
  export DEVFLOW_APPLE_AGENT_PLATFORM="$platform"
  export DEVFLOW_APPLE_AGENT_TIMEOUT_SECONDS="$apple_spike_timeout"
  export DEVFLOW_APPLE_IN_APP_AGENT_PORT="$apple_in_app_agent_port"
  export DEVFLOW_APPLE_TEST_SEED=devflow-sample-v1

  if [[ "$platform" == ios ]]; then
    if ! xcrun simctl bootstatus "$simulator_id" -b >"$xcode_raw" 2>&1; then
      redact_diagnostic_file "$xcode_raw" "$diagnostic_dir/apple-xctest-simulator.txt"
      rm -f -- "$xcode_raw"
      status=pending-spike
      classification=capability-missing
      omissions+=('{"kind":"apple-simulator","reason":"The requested iOS Simulator was not ready."}')
      return "$EXIT_PENDING"
    fi
    if ! xcrun simctl install "$simulator_id" "$apple_target_app" >"$xcode_raw" 2>&1; then
      redact_diagnostic_file "$xcode_raw" "$diagnostic_dir/apple-xctest-install.txt"
      rm -f -- "$xcode_raw"
      status=pending-spike
      classification=capability-missing
      omissions+=('{"kind":"apple-target-install","reason":"The built target app could not be installed on the selected simulator."}')
      return "$EXIT_PENDING"
    fi
    xcode_arguments+=(-destination "platform=iOS Simulator,id=$simulator_id")
  else
    # XCUITest activates the exact bundle ID. Opening first only makes the locally built
    # Mac Catalyst bundle discoverable; it does not claim foreground ownership.
    if ! open -g "$apple_target_app" >"$xcode_raw" 2>&1; then
      redact_diagnostic_file "$xcode_raw" "$diagnostic_dir/apple-xctest-open.txt"
      rm -f -- "$xcode_raw"
      status=pending-spike
      classification=capability-missing
      omissions+=('{"kind":"apple-target-launch","reason":"The built Mac Catalyst target app could not be opened."}')
      return "$EXIT_PENDING"
    fi
    xcode_arguments+=(-destination 'platform=macOS')
  fi

  [[ "$no_build" == true ]] && host_arguments+=(--no-build)
  target_digest=$(sha256_file "$(apple_info_plist)" 2>/dev/null || true)
  [[ -z "$target_digest" ]] || export DEVFLOW_TARGET_APP_DIGEST="$target_digest"
  host_arguments+=(-- --session-id "$DEVFLOW_APPLE_AGENT_SESSION_ID" --platform "$platform" \
    --target-bundle-id "$target_bundle_id" --artifact-root "$artifact_root" --ready-file "$host_ready" \
    --safe-action-id "$safe_action_id" --timeout-seconds "$apple_spike_timeout")
  [[ -z "$target_digest" ]] || host_arguments+=(--target-app-digest "$target_digest")

  temporary_paths+=("$host_raw" "$xcode_raw")
  umask 077
  dotnet "${host_arguments[@]}" >"$host_raw" 2>&1 &
  host_pid=$!

  local ready_attempt
  for (( ready_attempt = 0; ready_attempt < 30; ready_attempt++ )); do
    [[ -f "$host_ready" ]] && break
    if ! kill -0 "$host_pid" 2>/dev/null; then
      break
    fi
    sleep 1
  done
  if [[ ! -f "$host_ready" ]]; then
    wait "$host_pid"; host_exit=$?
    redact_diagnostic_file "$host_raw" "$diagnostic_dir/apple-xctest-host.txt"
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-xctest-host","reason":"The macOS host transport did not become ready without exposing its session secret."}')
    return "$EXIT_PENDING"
  fi

  endpoint=$(sed -n 's/.*"endpoint"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$host_ready" | head -n 1)
  if [[ -z "$endpoint" ]]; then
    kill -TERM "$host_pid" 2>/dev/null || true
    wait "$host_pid" || true
    redact_diagnostic_file "$host_raw" "$diagnostic_dir/apple-xctest-host.txt"
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-xctest-host","reason":"The macOS host readiness report did not contain a loopback endpoint."}')
    unset DEVFLOW_APPLE_AGENT_SESSION_SECRET
    return "$EXIT_PENDING"
  fi
  if ! is_loopback_endpoint "$endpoint"; then
    kill -TERM "$host_pid" 2>/dev/null || true
    wait "$host_pid" || true
    redact_diagnostic_file "$host_raw" "$diagnostic_dir/apple-xctest-host.txt"
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"apple-transport-endpoint","reason":"The host readiness report named a non-loopback endpoint, so the minted session secret was withheld."}')
    unset DEVFLOW_APPLE_AGENT_SESSION_SECRET
    return "$EXIT_PENDING"
  fi
  export DEVFLOW_APPLE_AGENT_ENDPOINT="$endpoint"

  # See the flow lane above: the array starts at the `test` subcommand and needs its driver named.
  xcodebuild "${xcode_arguments[@]}" >"$xcode_raw" 2>&1
  xcode_exit=$?
  wait "$host_pid"; host_exit=$?
  redact_diagnostic_file "$host_raw" "$diagnostic_dir/apple-xctest-host.txt"
  redact_diagnostic_file "$xcode_raw" "$diagnostic_dir/apple-xctest-xcode.txt"
  rm -f -- "$host_raw" "$xcode_raw"

  local spike_path="$artifact_root/apple-xctest-spike.json"
  if [[ "$host_exit" == 0 && "$xcode_exit" == 0 && -f "$spike_path" ]] &&
    grep -Eq '"status"[[:space:]]*:[[:space:]]*"proved"' "$spike_path"; then
    apple_spike_json=$(cat "$spike_path")
    status=passed
    classification=passed
    unset DEVFLOW_APPLE_AGENT_SESSION_SECRET DEVFLOW_APPLE_AGENT_ENDPOINT DEVFLOW_TARGET_APP_DIGEST
    return "$EXIT_SUCCESS"
  fi

  [[ -f "$spike_path" ]] && apple_spike_json=$(cat "$spike_path")
  status=pending-spike
  classification=proof-incomplete
  omissions+=('{"kind":"apple-xctest-proof","reason":"XCTest runtime proof was incomplete; inspect the bounded spike report and redacted host/Xcode diagnostics."}')
  unset DEVFLOW_APPLE_AGENT_SESSION_SECRET DEVFLOW_APPLE_AGENT_ENDPOINT DEVFLOW_TARGET_APP_DIGEST
  return "$EXIT_PENDING"
}

platform=
repeat=3
results_root_input=
configuration=Debug
flow_filter=
no_build=false
qualification=false
accumulate_directory=
baseline_path=
experimental=false
physical_device=false
device_id=
ios_runtime=
signing_identity=
provisioning_profile=
keychain=
verbosity=normal
dry_run=false
apple_spike=false
target_app=
target_bundle_id=
simulator_id=
safe_action_id=
apple_spike_timeout=180
apple_in_app_agent_port=9223

while (( $# > 0 )); do
  case "$1" in
    --help|-help)
      usage
      exit "$EXIT_SUCCESS"
      ;;
    --platform|-platform)
      require_value "$1" "${2-}"; platform=$2; shift 2
      ;;
    --repeat|-repeat)
      require_value "$1" "${2-}"; repeat=$2; shift 2
      ;;
    --results-root|-results-root)
      require_value "$1" "${2-}"; results_root_input=$2; shift 2
      ;;
    --configuration|-configuration)
      require_value "$1" "${2-}"; configuration=$2; shift 2
      ;;
    --flow-filter|-flow-filter)
      require_value "$1" "${2-}"; flow_filter=$2; shift 2
      ;;
    --no-build|-no-build)
      no_build=true; shift
      ;;
    --qualification|-qualification)
      qualification=true; shift
      ;;
    --accumulate|-accumulate)
      require_value "$1" "${2-}"; accumulate_directory=$2; shift 2
      ;;
    --baseline|-baseline)
      require_value "$1" "${2-}"; baseline_path=$2; shift 2
      ;;
    --experimental|-experimental)
      experimental=true; shift
      ;;
    --physical-device|-physical-device)
      physical_device=true; shift
      ;;
    --device-id|-device-id)
      require_value "$1" "${2-}"; device_id=$2; shift 2
      ;;
    --ios-runtime|-ios-runtime)
      require_value "$1" "${2-}"; ios_runtime=$2; shift 2
      ;;
    --signing-identity|--ios-signing-identity)
      require_value "$1" "${2-}"; signing_identity=$2; shift 2
      ;;
    --provisioning-profile|--ios-provisioning-profile)
      require_value "$1" "${2-}"; provisioning_profile=$2; shift 2
      ;;
    --keychain|--ios-keychain)
      require_value "$1" "${2-}"; keychain=$2; shift 2
      ;;
    --apple-spike)
      apple_spike=true; shift
      ;;
    --target-app)
      require_value "$1" "${2-}"; target_app=$2; shift 2
      ;;
    --target-bundle-id)
      require_value "$1" "${2-}"; target_bundle_id=$2; shift 2
      ;;
    --simulator-id)
      require_value "$1" "${2-}"; simulator_id=$2; shift 2
      ;;
    --safe-action-id)
      require_value "$1" "${2-}"; safe_action_id=$2; shift 2
      ;;
    --apple-spike-timeout)
      require_value "$1" "${2-}"; apple_spike_timeout=$2; shift 2
      ;;
    --apple-in-app-agent-port)
      require_value "$1" "${2-}"; apple_in_app_agent_port=$2; shift 2
      ;;
    --verbosity|-verbosity)
      require_value "$1" "${2-}"; verbosity=$2; shift 2
      ;;
    --verbose)
      verbosity=detailed; shift
      ;;
    --dry-run|-dry-run)
      dry_run=true; shift
      ;;
    *)
      die_usage "Unknown option '$1'."
      ;;
  esac
done

platform=${platform,,}
[[ "$platform" =~ ^(android|windows|ios|maccatalyst|macos)$ ]] ||
  die_usage '--platform is required and must be android, windows, ios, maccatalyst, or macos.'
[[ -n "$results_root_input" ]] || die_usage '--results-root is required.'
[[ "$repeat" =~ ^[0-9]+$ ]] && (( 10#$repeat >= 1 && 10#$repeat <= 20 )) ||
  die_usage '--repeat must be an integer from 1 through 20. Use --accumulate to merge evidence across independent runs instead of raising this cap.'
repeat=$((10#$repeat))
[[ -z "$accumulate_directory" || "$qualification" == true ]] ||
  die_usage '--accumulate requires --qualification.'
[[ -z "$baseline_path" || "$qualification" == true ]] ||
  die_usage '--baseline requires --qualification.'
validate_single_line '--configuration' "$configuration"
[[ "$configuration" =~ ^[A-Za-z0-9._-]+$ ]] ||
  die_usage '--configuration may contain only letters, digits, dot, underscore, and hyphen.'
[[ "$verbosity" =~ ^(quiet|minimal|normal|detailed|diagnostic)$ ]] ||
  die_usage '--verbosity must be quiet, minimal, normal, detailed, or diagnostic.'
[[ -z "$flow_filter" ]] || validate_single_line '--flow-filter' "$flow_filter"
[[ -z "$device_id" ]] || validate_single_line '--device-id' "$device_id"
[[ -z "$ios_runtime" ]] || validate_single_line '--ios-runtime' "$ios_runtime"
[[ -z "$signing_identity" ]] || validate_single_line '--signing-identity' "$signing_identity"
[[ -z "$provisioning_profile" ]] || validate_single_line '--provisioning-profile' "$provisioning_profile"
[[ -z "$keychain" ]] || validate_single_line '--keychain' "$keychain"
[[ -z "$target_app" ]] || validate_single_line '--target-app' "$target_app"
[[ -z "$target_bundle_id" ]] || validate_single_line '--target-bundle-id' "$target_bundle_id"
[[ -z "$simulator_id" ]] || validate_single_line '--simulator-id' "$simulator_id"
[[ -z "$safe_action_id" ]] || validate_single_line '--safe-action-id' "$safe_action_id"
[[ "$apple_spike_timeout" =~ ^[0-9]+$ ]] && (( 10#$apple_spike_timeout >= 15 && 10#$apple_spike_timeout <= 600 )) ||
  die_usage '--apple-spike-timeout must be an integer from 15 through 600.'
apple_spike_timeout=$((10#$apple_spike_timeout))
[[ "$apple_in_app_agent_port" =~ ^[0-9]+$ ]] && (( 10#$apple_in_app_agent_port >= 1024 && 10#$apple_in_app_agent_port <= 65535 )) ||
  die_usage '--apple-in-app-agent-port must be an integer from 1024 through 65535.'
apple_in_app_agent_port=$((10#$apple_in_app_agent_port))

[[ "$platform" != macos || "$experimental" == true ]] ||
  die_usage '--platform macos is experimental and requires --experimental.'
[[ "$experimental" != true || "$platform" == macos ]] ||
  die_usage '--experimental applies only to the separately labeled macos/AppKit lane.'
[[ "$platform" != macos || "$qualification" != true ]] ||
  die_usage '--qualification cannot be used for experimental AppKit; it never qualifies an official MAUI or Mac Catalyst gate.'
[[ "$physical_device" != true || "$platform" == ios ]] ||
  die_usage '--physical-device applies only to --platform ios.'
[[ "$physical_device" != true || -z "$ios_runtime" ]] ||
  die_usage '--ios-runtime is a simulator selector and cannot be combined with --physical-device.'
[[ -z "$ios_runtime" || "$platform" == ios ]] ||
  die_usage '--ios-runtime applies only to the iOS Simulator lane.'
[[ -z "$device_id" || "$platform" == android || ("$platform" == ios && "$physical_device" == true) ]] ||
  die_usage '--device-id applies to Android or to the physical-iOS lane.'
if [[ "$platform" == ios && "$physical_device" == true ]]; then
  [[ -n "$device_id" ]] || die_usage 'Physical iOS requires --device-id.'
  [[ -n "$signing_identity" ]] || die_usage 'Physical iOS requires --signing-identity.'
  [[ -n "$provisioning_profile" ]] || die_usage 'Physical iOS requires --provisioning-profile.'
  [[ -n "$keychain" ]] || die_usage 'Physical iOS requires --keychain.'
elif [[ -n "$signing_identity$provisioning_profile$keychain" ]]; then
  die_usage 'Signing, provisioning, and keychain options apply only to --platform ios --physical-device.'
fi
if [[ "$apple_spike" == true ]]; then
  [[ "$platform" == ios || "$platform" == maccatalyst || "$platform" == macos ]] ||
    die_usage '--apple-spike supports only the iOS Simulator, Mac Catalyst, and experimental AppKit proof lanes.'
  [[ "$physical_device" != true ]] ||
    die_usage '--apple-spike does not claim physical-iOS proof; use the separately provisioned physical lane after this spike.'
fi
[[ -z "$simulator_id" || "$platform" == ios ]] ||
  die_usage '--simulator-id applies only to the iOS Simulator lane.'

if [[ "$platform" =~ ^(ios|maccatalyst|macos)$ && "$physical_device" != true ]]; then
  if [[ "$platform" == macos ]]; then
    target_bundle_id=${target_bundle_id:-com.companyname.mauitodo.appkit}
  else
    target_bundle_id=${target_bundle_id:-com.companyname.mauitodo}
  fi
  safe_action_id=${safe_action_id:-AddButton}
  [[ "$target_bundle_id" =~ ^[A-Za-z0-9.-]+$ ]] ||
    die_usage '--target-bundle-id may contain only letters, digits, dot, and hyphen.'
fi

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
repo_root=$(cd -- "$script_dir/../.." && pwd -P)
register_secret_value "$signing_identity"
register_secret_value "$provisioning_profile"
register_secret_value "$keychain"
register_secret_value "${DEVFLOW_APPLE_AGENT_SESSION_SECRET-}"

[[ -f "$repo_root/global.json" && -f "$repo_root/MauiLabs.slnx" ]] || {
  printf 'flow-qa: The script must be run from a maui-labs checkout with global.json.\n' >&2
  exit "$EXIT_PREREQUISITE"
}

if [[ -n "${DEVFLOW_FLOW_QA_RUN_ID-}" ]]; then
  [[ "$DEVFLOW_FLOW_QA_RUN_ID" =~ ^[A-Za-z0-9._-]+$ ]] ||
    die_usage 'DEVFLOW_FLOW_QA_RUN_ID may contain only letters, digits, dot, underscore, and hyphen.'
  run_id=$DEVFLOW_FLOW_QA_RUN_ID
elif [[ -n "${GITHUB_RUN_ID-}" ]]; then
  run_id="$GITHUB_RUN_ID${GITHUB_RUN_ATTEMPT:+-$GITHUB_RUN_ATTEMPT}"
else
  run_id="local-$(date -u '+%Y%m%d%H%M%S')-$$"
fi

resolve_results_root "$results_root_input"
results_root=$resolved_results_root
resolve_artifact_root
artifact_root=$resolved_artifact_root
diagnostic_dir="$artifact_root/host-diagnostics"
manifest_path="$artifact_root/manifest.json"
flow_run_path="$artifact_root/flow-run.json"
test_project="$repo_root/src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/Microsoft.Maui.DevFlow.Agent.IntegrationTests.csproj"
if [[ "$platform" == macos ]]; then
  app_project="$repo_root/samples/DevFlow.Sample.MacOS/DevFlow.Sample.MacOS.csproj"
  flow_directory="$repo_root/samples/DevFlow.Sample.MacOS/maui-tests"
else
  app_project="$repo_root/samples/DevFlow.Sample/DevFlow.Sample.csproj"
  flow_directory="$repo_root/samples/DevFlow.Sample/maui-tests"
fi
apple_agent_root="$repo_root/src/DevFlow/Microsoft.Maui.DevFlow.TestAgent"
apple_agent_host_project="$repo_root/src/DevFlow/Microsoft.Maui.DevFlow.TestAgent.Host/Microsoft.Maui.DevFlow.TestAgent.Host.csproj"
apple_agent_native_project="$apple_agent_root/AppleXCTestAgent/DevFlowAppleTestAgent.xcodeproj"
apple_agent_swift_source="$apple_agent_root/AppleXCTestAgent/DevFlowAppleTestAgentUITests/DevFlowAppleTestAgentUITests.swift"
apple_agent_source_available=false
[[ -f "$apple_agent_host_project" && -f "$apple_agent_native_project/project.pbxproj" && -f "$apple_agent_swift_source" ]] &&
  apple_agent_source_available=true
apple_target_app=
if [[ -n "$target_app" ]]; then
  if [[ "$target_app" = /* ]]; then
    apple_target_app=${target_app%/}
  else
    apple_target_app="$repo_root/${target_app%/}"
  fi
fi
apple_qa_manifest_path="$artifact_root/apple-flow-qa.json"
apple_simulator_runtime=${ios_runtime:-default}
apple_simulator_profile=not-observed
apple_reset_fingerprint=not-observed
apple_seed_fingerprint=not-observed
apple_backend_fingerprint=not-observed

case "$platform" in
  android) base_filter='Category=FlowPilot' ;;
  windows) base_filter='Category=WindowsFlowQa' ;;
  macos) base_filter='Category=AppKitFlowQa' ;;
  *) base_filter='Category=AppleTestAgent' ;;
esac
if [[ -n "$flow_filter" ]]; then
  test_filter="$base_filter&($flow_filter)"
else
  test_filter=$base_filter
fi
manifest_experimental=false
manifest_backend=null
manifest_official_coverage=true
manifest_maccatalyst_equivalent=null
if [[ "$platform" == macos ]]; then
  manifest_experimental=true
  manifest_backend=$(json_string appkit)
  manifest_official_coverage=false
  manifest_maccatalyst_equivalent=false
fi
test_filter_digest=$(sha256_string "$test_filter" 2>/dev/null || printf unknown)
if [[ "$platform" == android || "$platform" == windows ]]; then
  trx_file_name="devflow-flow-$platform-$run_id.trx"
else
  trx_file_name="devflow-flow-$platform-$run_id-attempt-{attempt}.trx"
fi
test_arguments=(test "$test_project" --configuration "$configuration" --filter "$test_filter"
  --logger "trx;LogFileName=$trx_file_name"
  --logger "console;verbosity=$verbosity"
  --results-directory "$results_root")
[[ "$no_build" == true ]] && test_arguments+=(--no-build)

temporary_paths=()
trap cleanup_temporary_paths EXIT

if [[ "$dry_run" == true ]]; then
  printf '{"schema":1,"kind":"devflow-flow-qa-dry-run","status":"dry-run","platform":'
  json_string "$platform"
  printf ',"repeat":%s,"configuration":' "$repeat"
  json_string "$configuration"
  printf ',"testFilter":'
  json_string "$test_filter"
  printf ',"noBuild":%s,"qualificationRequested":%s,"experimental":%s,"physicalDevice":%s,"signingInputsConfigured":%s' \
    "$no_build" "$qualification" "$experimental" "$physical_device" \
    "$([[ -n "$signing_identity" && -n "$provisioning_profile" && -n "$keychain" ]] && printf true || printf false)"
  printf ',"backend":%s,"officialCoverage":%s,"macCatalystEquivalent":%s' \
    "$manifest_backend" "$manifest_official_coverage" "$manifest_maccatalyst_equivalent"
  printf ',"appProject":'
  json_string "$(repo_relative "$app_project")"
  local_apple_runtime=false
  [[ "$platform" == ios || "$platform" == maccatalyst || "$platform" == macos ]] && local_apple_runtime=true
  printf ',"appleSpike":%s' "$local_apple_runtime"
  if [[ "$local_apple_runtime" == true ]]; then
    printf ',"appleTarget":{"app":'
    if [[ -n "$apple_target_app" ]]; then json_string "$apple_target_app"; else printf 'null'; fi
    printf ',"bundleId":'
    if [[ -n "$target_bundle_id" ]]; then json_string "$target_bundle_id"; else printf 'null'; fi
    printf ',"simulatorIdConfigured":%s,"safeActionId":' "$([[ -n "$simulator_id" ]] && printf true || printf false)"
    if [[ -n "$safe_action_id" ]]; then json_string "$safe_action_id"; else printf 'null'; fi
    printf '}'
  fi
  printf ',"command":{"tool":"dotnet","arguments":'
  if [[ "$local_apple_runtime" == true ]]; then
    json_array run --project "$apple_agent_host_project" --configuration "$configuration" -- --session-id "apple-$run_id" --platform "$platform" --target-bundle-id "$target_bundle_id" --artifact-root "$artifact_root" --ready-file "$artifact_root/apple-xctest-host-ready.json" --safe-action-id "$safe_action_id" --timeout-seconds "$apple_spike_timeout"
  else
    json_array "${test_arguments[@]}"
  fi
  printf '},"artifactPaths":{"testResults":'
  json_string "$(repo_relative "$results_root")"
  printf ',"artifactRoot":'
  json_string "$(repo_relative "$artifact_root")"
  printf ',"manifest":'
  json_string "$(repo_relative "$manifest_path")"
  printf ',"flowRun":'
  json_string "$(repo_relative "$flow_run_path")"
  printf '},"capability":{"required":'
  if [[ "$platform" =~ ^(ios|maccatalyst|macos)$ ]]; then
    json_string 'apple-test-agent'
    printf ',"sourceAvailable":%s,"available":false,"state":' "$apple_agent_source_available"
    if [[ "$local_apple_runtime" == true && "$apple_agent_source_available" == true ]]; then json_string proof-required; else json_string pending-spike; fi
  else
    json_string 'platform-fixture'
    printf ',"available":true,"state":"planned"'
  fi
  printf '}}\n'
  exit "$EXIT_SUCCESS"
fi

mkdir -p -- "$results_root" "$artifact_root" "$diagnostic_dir"
canonical_results=$(cd -- "$results_root" && pwd -P)
canonical_expected=$(cd -- "$repo_root/artifacts/TestResults/devflow-flow/$platform" && pwd -P)
[[ "$canonical_results" == "$canonical_expected" ]] ||
  die_usage 'The resolved results path traversed a symbolic link.'
[[ ! -e "$manifest_path" && ! -e "$flow_run_path" ]] || {
  printf "flow-qa: Refusing to overwrite existing run artifacts for '%s'.\n" "$run_id" >&2
  exit "$EXIT_USAGE"
}

repository_commit=$(git -C "$repo_root" rev-parse HEAD 2>/dev/null || printf unknown)
host_os=$(uname -srm)
dotnet_sdk=unknown
xcode_version=not-applicable
device_kind=desktop-host
if command -v dotnet >/dev/null 2>&1; then
  dotnet_sdk=$(dotnet --version 2>/dev/null || printf unknown)
fi
if [[ "$(uname -s)" == Darwin ]]; then
  xcode_version=unavailable
  if command -v xcodebuild >/dev/null 2>&1; then
    xcode_version=$(xcodebuild -version 2>/dev/null | tr '\n' ';' || printf unavailable)
  fi
fi
case "$platform" in
  android) device_kind=emulator ;;
  ios)
    if [[ "$physical_device" == true ]]; then device_kind=physical-device; else device_kind=simulator; fi
    ;;
esac
device_id_digest=
[[ -z "$device_id" ]] || device_id_digest=$(sha256_string "$device_id" 2>/dev/null || true)
device_id_json=null
[[ -z "$device_id_digest" ]] || device_id_json=$(json_string "$device_id_digest")
signing_refs_json='{"physical":false,"identityRef":null,"provisioningProfileRef":null,"keychainRef":null}'
if [[ "$physical_device" == true ]]; then
  signing_refs_json="{\"physical\":true,\"identityRef\":$([[ -n "$signing_identity" ]] && json_string "$(sha256_string "$signing_identity")" || printf null),\"provisioningProfileRef\":$([[ -n "$provisioning_profile" ]] && json_string "$(sha256_string "$provisioning_profile")" || printf null),\"keychainRef\":$([[ -n "$keychain" ]] && json_string "$(sha256_string "$keychain")" || printf null)}"
fi

attempts=()
omissions=('{"kind":"diagnostic-rerun","reason":"No automatic diagnostic rerun was performed because replay may mutate state."}')
write_omissions=()
artifact_records=()
artifact_omissions=()
artifact_omitted_by_limit=0
artifact_omitted_outside_repository=0
artifact_omitted_unhashable=0
artifact_enumeration_errors=0
preserved_manifest_relative=
preserved_manifest_reason=
preserved_manifest_record=
preserved_manifest_replaced=0
artifact_summary_json='{"maxArtifacts":'"$MAX_ARTIFACTS"',"recordedArtifacts":0,"omittedArtifacts":0,"truncated":false}'
diagnostic_truncated=false
command_exit=1
command_output=
command_classification=pending
command_classification_source=none
command_truncated=false
qualification_json=null
apple_spike_json=null
apple_qa_json=null
status=pending
classification=pending

if ! command -v dotnet >/dev/null 2>&1; then
  status=failed
  classification=prerequisite-missing
  omissions+=('{"kind":"prerequisite","reason":"dotnet was not found. The script does not install SDKs or workloads."}')
  write_artifacts
  print_status
  exit "$EXIT_PREREQUISITE"
fi
if [[ ! -f "$test_project" ]]; then
  status=failed
  classification=prerequisite-missing
  omissions+=('{"kind":"test-project","reason":"The integration test project was unavailable."}')
  write_artifacts
  print_status
  exit "$EXIT_PREREQUISITE"
fi

host_kernel=$(uname -s)
if [[ "$platform" == windows && "$host_kernel" != MINGW* && "$host_kernel" != MSYS* && "$host_kernel" != CYGWIN* ]]; then
  status=failed
  classification=prerequisite-missing
  omissions+=('{"kind":"host-platform","reason":"The windows lane requires a Windows host."}')
  write_artifacts
  print_status
  exit "$EXIT_PREREQUISITE"
fi
if [[ "$platform" =~ ^(ios|maccatalyst|macos)$ && "$host_kernel" != Darwin ]]; then
  status=failed
  classification=prerequisite-missing
  omissions+=("{\"kind\":\"host-platform\",\"reason\":\"The '$platform' lane requires a macOS host.\"}")
  write_artifacts
  print_status
  exit "$EXIT_PREREQUISITE"
fi
if [[ "$platform" == macos && ! -f "$app_project" ]]; then
  status=unsupported
  classification=unsupported-platform
  omissions+=('{"kind":"appkit-sample","reason":"No experimental AppKit sample or fixture project is available."}')
  write_artifacts
  print_status
  exit "$EXIT_PENDING"
fi

cd -- "$repo_root"
if [[ "$platform" == ios || "$platform" == maccatalyst || "$platform" == macos ]]; then
  if [[ "$physical_device" == true ]]; then
    status=pending-spike
    classification=capability-missing
    omissions+=('{"kind":"physical-ios-flow-qa","reason":"The simulator/Mac Catalyst XCTest agent proof does not certify the separately provisioned physical-iOS lane."}')
    write_artifacts
    print_status
    exit "$EXIT_PENDING"
  fi

  # Every non-dry-run official Apple invocation establishes fresh foreground/auth/cancellation/
  # parity evidence before it can execute the Tier-1 corpus. A checked-in source tree is never
  # treated as a capability report.
  run_apple_spike
  apple_spike_exit=$?
  if (( apple_spike_exit != EXIT_SUCCESS )); then
    write_artifacts
    print_status
    exit "$EXIT_PENDING"
  fi

  run_apple_flow_qa
  apple_qa_exit=$?
  if [[ "$platform" == macos ]]; then
    export DEVFLOW_RUN_APPKIT_FLOW_QA=1
    export DEVFLOW_APPKIT_QA_MANIFEST="$apple_qa_manifest_path"
  else
    export DEVFLOW_RUN_APPLE_FLOW_QA=1
    export DEVFLOW_APPLE_QA_MANIFEST="$apple_qa_manifest_path"
  fi
  run_test_command 1
  attempts+=("$(attempt_json clean 1)")

  if (( apple_qa_exit != 0 )); then
    status=failed
    classification=flow-failure
  elif (( command_exit != 0 )); then
    status=failed
    classification=$command_classification
  else
    status=passed
    classification=passed
  fi
  write_artifacts
  if [[ "$qualification" == true ]]; then
    # Apple evidence is adapted read-only for review. The Android preview policy remains
    # intentionally non-authoritative for iOS, Mac Catalyst, and experimental AppKit, so its
    # not-qualified result cannot relabel a completed Apple runtime attempt.
    run_qualification || true
    # Register the explicit, read-only qualification output in the final hash manifest without
    # converting its advisory result into Apple runtime status. The whole write pass runs again so
    # both reports state the same artifact facts about the same set of files.
    write_artifacts
  fi
  print_status
  case "$classification" in
    passed) exit "$EXIT_SUCCESS" ;;
    flow-failure) exit "$EXIT_FLOW_FAILURE" ;;
    *) exit "$EXIT_PREREQUISITE" ;;
  esac
fi

if [[ "$platform" == android ]]; then
  export DEVFLOW_TEST_PLATFORM=android
  export DEVFLOW_RUN_ANDROID_FLOW_PILOT=1
  export DEVFLOW_FLOW_PILOT_REPEAT="$repeat"
  export DEVFLOW_FLOW_PILOT_ARTIFACT_ROOT="$artifact_root"
  export DEVFLOW_FLOW_PILOT_RESULTS_ROOT="$results_root"
  export DEVFLOW_FLOW_PILOT_WORKFLOW_RUN_ID="$run_id"
  export DEVFLOW_FLOW_PILOT_REPOSITORY_COMMIT="$repository_commit"
  export DEVFLOW_FLOW_PILOT_DEVICE_EVIDENCE_KIND=emulator
  [[ -z "$device_id" ]] || export DEVFLOW_TEST_ANDROID_SERIAL="$device_id"
  run_test_command 1
  attempts+=("$(attempt_json clean 1)")
elif [[ "$platform" == windows ]]; then
  export DEVFLOW_TEST_PLATFORM=windows
  export DEVFLOW_RUN_WINDOWS_FLOW_QA=1
  export DEVFLOW_FLOW_QA_RUN_ID="$run_id"
  export DEVFLOW_FLOW_QA_REPEAT="$repeat"
  export DEVFLOW_FLOW_QA_ARTIFACT_ROOT="$artifact_root"
  export DEVFLOW_FLOW_QA_APP_PROJECT="$app_project"
  # WindowsFixture owns clean per-flow repetitions; do not multiply repeat values here.
  run_test_command 1
  attempts+=("$(attempt_json tier-1-corpus 1 ",\"cleanRepetitionsPerFlow\":$repeat")")
else
  export DEVFLOW_TEST_PLATFORM="$platform"
  export DEVFLOW_FLOW_QA_RUN_ID="$run_id"
  export DEVFLOW_FLOW_QA_REPEAT="$repeat"
  export DEVFLOW_FLOW_QA_APP_PROJECT="$app_project"
  [[ -z "$ios_runtime" ]] || export DEVFLOW_TEST_IOS_VERSION="$ios_runtime"
  if [[ "$physical_device" == true ]]; then
    export DEVFLOW_FLOW_QA_PHYSICAL_DEVICE=1
    export DEVFLOW_FLOW_QA_DEVICE_ID="$device_id"
    export DEVFLOW_IOS_SIGNING_IDENTITY="$signing_identity"
    export DEVFLOW_IOS_PROVISIONING_PROFILE="$provisioning_profile"
    export DEVFLOW_IOS_KEYCHAIN="$keychain"
  fi
  for (( attempt = 1; attempt <= repeat; attempt++ )); do
    run_test_command "$attempt"
    attempts+=("$(attempt_json clean "$attempt")")
  done
fi

if printf '%s\n' "${attempts[@]}" | grep -q 'capability-missing'; then
  status=capability-missing
  classification=capability-missing
elif printf '%s\n' "${attempts[@]}" | grep -q 'infrastructure-failure'; then
  status=failed
  classification=infrastructure-failure
elif printf '%s\n' "${attempts[@]}" | grep -q 'flow-failure'; then
  status=failed
  classification=flow-failure
else
  status=passed
  classification=passed
fi
write_artifacts

if [[ "$qualification" == true && "$classification" == passed ]]; then
  run_qualification
  qualification_exit=$?
  if (( qualification_exit == EXIT_PENDING )); then
    status=not-qualified
    classification=not-qualified
  elif (( qualification_exit != 0 )); then
    status=failed
    classification=infrastructure-failure
  fi
  write_artifacts
elif [[ "$qualification" == true ]]; then
  omissions+=('{"kind":"qualification","reason":"Qualification was not run because platform execution did not pass."}')
  write_artifacts
fi

print_status
case "$classification" in
  passed) exit "$EXIT_SUCCESS" ;;
  flow-failure) exit "$EXIT_FLOW_FAILURE" ;;
  capability-missing) exit "$EXIT_PENDING" ;;
  not-qualified) exit "$EXIT_PENDING" ;;
  *) exit "$EXIT_PREREQUISITE" ;;
esac
