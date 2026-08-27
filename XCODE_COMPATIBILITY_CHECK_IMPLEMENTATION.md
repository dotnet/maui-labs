# Xcode Compatibility Check Implementation

## Overview

This document describes the implementation of the Xcode Compatibility Check feature for the MAUI CLI. The feature detects and fixes Xcode version mismatches with installed iOS/macOS SDK packs.

## Files Modified/Created

### New Files
1. **src/Cli/Microsoft.Maui.Cli/Providers/Apple/XcodeCompatibilityChecker.cs**
   - Main checker class that detects Apple SDK packs and their Xcode requirements
   - Parses MSBuild properties to extract required Xcode versions
   - Compares installed Xcode versions with requirements
   - Provides auto-fix recommendations

2. **src/Cli/Microsoft.Maui.Cli.UnitTests/Providers/Apple/XcodeCompatibilityCheckerTests.cs**
   - Unit tests for XcodeCompatibilityChecker
   - Tests version extraction logic
   - Tests compatibility detection with fake XcodeManager

3. **src/Cli/Microsoft.Maui.Cli/ManualTests/XcodeCompatibilityTestSandbox.cs**
   - Manual test sandbox app for end-to-end verification
   - Tests direct checker usage
   - Tests AppleProvider integration
   - Tests DoctorService integration
   - Tests fix application

### Modified Files
1. **src/Cli/Microsoft.Maui.Cli/Errors/ErrorCodes.cs**
   - Added error code `E2224` for Xcode version mismatch

2. **src/Cli/Microsoft.Maui.Cli/Providers/Apple/AppleProvider.cs**
   - Extended `CheckHealth()` method to include Xcode compatibility check
   - Calls XcodeCompatibilityChecker after license check, only if Xcode is present

3. **src/Cli/Microsoft.Maui.Cli.UnitTests/DoctorServiceTests.cs**
   - Added tests for TryFixAsync method
   - Added tests for ParseCommand and TokenizeArguments methods
   - Added test for Xcode compatibility check integration

## Feature Architecture

### Detection Flow

```
XcodeCompatibilityChecker.CheckXcodeCompatibility()
├── Get selected Xcode version
├── Detect SDK packs in /usr/local/share/dotnet/packs/
├── Parse MSBuild properties for _RecommendedXcodeVersion
├── Compare selected version with requirements
└── Return HealthCheck with compatibility status
```

### Integration Points

1. **AppleProvider.CheckHealth()** - Automatically called by CLI
2. **DoctorService.RunAllChecksAsync()** - Aggregates all checks
3. **DoctorService.TryFixAsync()** - Applies auto-fixes
4. **DoctorCommand.cs** - CLI integration with --fix flag (already implemented)

### Fix Application

When an Xcode version mismatch is detected and a compatible version is installed:
- Status: **Error** (incompatible SDKs)
- AutoFixable: **true**
- Command: `xcode-select -s /path/to/matching/Xcode`
- User can apply with: `maui doctor --fix`

When no compatible version is installed:
- Status: **Error**
- AutoFixable: **false**
- Manual steps provided

## Implementation Details

### SDK Detection
- Scans `/usr/local/share/dotnet/packs/` for SDK directories
- Looks for pattern: `Microsoft.{Platform}.Sdk.net{TFM}_{Version}`
- Extracts platform name (iOS, macOS, tvOS, watchOS)

### Version Extraction
- Reads MSBuild properties file: `targets/Microsoft.*.Sdk.Versions.props`
- Extracts `_RecommendedXcodeVersion` element
- Normalizes to major.minor format (e.g., "26.5" from "26.5.1")

### Compatibility Check
- Compares selected Xcode major.minor with each SDK's requirement
- Marks as compatible only if versions match
- Provides detailed incompatibility list in JSON output

### Auto-Fix Logic
- Uses XcodeManager.List() to find all installed Xcode versions
- Searches for matching version by major.minor
- If found, suggests `xcode-select -s /path` command
- If not found, provides manual steps

## Testing Strategy

### Unit Tests
1. **Version Extraction**
   - Three-part versions (26.5.1 → 26.5)
   - Two-part versions (26.5 → 26.5)
   - Single-part versions (26 → 26)
   - Null/empty versions (→ null)

2. **Compatibility Detection**
   - No XcodeManager → Skipped
   - No SDK packs → Skipped
   - Compatible versions → Ok
   - Incompatible versions → Error

3. **Fix Application**
   - AutoFixable with valid command → Success
   - Non-AutoFixable → Failure
   - Invalid command → Failure

4. **Command Parsing**
   - Simple commands
   - Commands with quoted paths
   - Commands with multiple arguments

### Integration Tests
1. **AppleProvider.CheckHealth()**
   - Includes Xcode Compatibility check
   - Check appears after Xcode License check
   - Only included when Xcode is present

2. **DoctorService**
   - Aggregates compatibility check
   - --fix flag applies auto-fixable checks
   - Re-runs checks after fixes to verify

3. **Full CLI Flow**
   - `maui doctor` shows compatibility status
   - `maui doctor --json` includes compatibility in JSON
   - `maui doctor --fix` attempts auto-fix

## Manual Testing

Run the test sandbox:
```bash
# On macOS only
cd src/Cli/Microsoft.Maui.Cli
dotnet run --project ManualTests/XcodeCompatibilityTestSandbox.cs
```

This will:
1. Test direct XcodeCompatibilityChecker usage
2. Test AppleProvider integration
3. Test DoctorService integration
4. Test fix application

## CLI Usage

```bash
# Check status
maui doctor

# Check with JSON output
maui doctor --json

# Auto-fix incompatibilities
maui doctor --fix

# Check specific category
maui doctor --platform apple
```

## Future Enhancements

1. **Caching**: Cache SDK detection results for performance
2. **Version Range Support**: Handle version ranges (e.g., "26.4 - 26.6")
3. **Multiple Fix Options**: Suggest multiple compatible Xcode versions
4. **Metrics**: Track how often this check helps users
5. **Notifications**: Notify when SDK packs update their requirements

## Error Codes

- **E2224**: Xcode version mismatch with installed SDK packs
- Severity: Error (blocks development)
- Auto-fixable: When compatible version is installed

## Known Limitations

1. Requires MSBuild properties to be present in SDK packs
2. Only works on macOS (returns Skipped on other platforms)
3. Detects only locally installed Xcode versions
4. Version comparison is major.minor only (ignores patch version)

## Testing Checklist

- [ ] Unit tests pass: `dotnet test src/Cli/Microsoft.Maui.Cli.UnitTests/`
- [ ] XcodeCompatibilityCheckerTests pass
- [ ] DoctorServiceTests pass
- [ ] No regressions in existing tests
- [ ] Manual test sandbox runs successfully
- [ ] CLI integration: `maui doctor` shows check
- [ ] CLI integration: `maui doctor --json` includes check
- [ ] Auto-fix: `maui doctor --fix` applies command
- [ ] Error handling: Graceful fallback for missing SDK packs
- [ ] Non-macOS: Returns Skipped on other platforms
