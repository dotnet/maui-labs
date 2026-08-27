# Xcode Compatibility Check - Quick Start Guide

## Overview
The Xcode Compatibility Check feature automatically detects if your installed Xcode version matches the requirements of your iOS/macOS SDK packs. It can automatically fix mismatches when a compatible Xcode is available.

## Files Summary

### Production Code (3 files created)
```
✅ src/Cli/Microsoft.Maui.Cli/Providers/Apple/XcodeCompatibilityChecker.cs
   ├─ Main detection engine
   ├─ Scans /usr/local/share/dotnet/packs/ for SDK packs
   ├─ Parses MSBuild properties for Xcode requirements
   └─ Suggests auto-fixes when compatible versions available

✅ src/Cli/Microsoft.Maui.Cli.UnitTests/Providers/Apple/XcodeCompatibilityCheckerTests.cs
   ├─ 10 unit tests
   ├─ Version extraction tests (3-part, 2-part, 1-part, null/empty)
   ├─ Compatibility detection tests
   └─ HealthCheck structure tests

✅ src/Cli/Microsoft.Maui.Cli/ManualTests/XcodeCompatibilityTestSandbox.cs
   ├─ Comprehensive manual test app
   ├─ Tests direct checker usage
   ├─ Tests AppleProvider integration
   ├─ Tests DoctorService integration
   └─ Tests fix application
```

### Production Code (3 files modified)
```
✅ src/Cli/Microsoft.Maui.Cli/Errors/ErrorCodes.cs
   └─ Added: AppleXcodeVersionMismatch = "E2224"

✅ src/Cli/Microsoft.Maui.Cli/Providers/Apple/AppleProvider.cs
   └─ Added: XcodeCompatibilityChecker integration in CheckHealth()

✅ src/Cli/Microsoft.Maui.Cli.UnitTests/DoctorServiceTests.cs
   └─ Added: 7 new test methods for comprehensive coverage
```

### Documentation (2 files)
```
📄 IMPLEMENTATION_SUMMARY.md
   └─ Complete implementation overview

📄 XCODE_COMPATIBILITY_CHECK_IMPLEMENTATION.md
   └─ Detailed technical documentation
```

## Quick Test

### 1. Unit Tests (All Platforms)
```bash
cd /Users/jfversluis/.copilot/copilot-worktrees/maui-labs/jfversluis-vigilant-umbrella

# Run all tests
dotnet test src/Cli/Microsoft.Maui.Cli.UnitTests/

# Run Xcode compatibility tests only
dotnet test src/Cli/Microsoft.Maui.Cli.UnitTests/ -k XcodeCompatibility

# Run doctor service tests only  
dotnet test src/Cli/Microsoft.Maui.Cli.UnitTests/ -k DoctorService
```

### 2. Manual Test (macOS only)
```bash
# Test sandbox demonstrates all features
cd src/Cli/Microsoft.Maui.Cli
dotnet run --project ManualTests/XcodeCompatibilityTestSandbox.cs
```

### 3. CLI Integration (macOS only)
```bash
# Show all health checks including Xcode compatibility
maui doctor

# JSON output
maui doctor --json

# Auto-fix incompatibilities
maui doctor --fix

# Apple-only checks
maui doctor --platform apple
```

## Key Features

### Detection (Automatic)
- ✅ Scans for all Apple SDK packs
- ✅ Extracts required Xcode versions from MSBuild properties
- ✅ Compares with installed Xcode
- ✅ Reports incompatibilities

### Auto-Fix (When Available)
- ✅ Checks if compatible Xcode is installed
- ✅ Provides `xcode-select -s /path` command
- ✅ Applies with `maui doctor --fix`
- ✅ Re-verifies after fix

### Output
```
Category: apple
Name: Xcode Compatibility
Status: error (or ok/skipped)
Message: "SDK packs require Xcode 26.5, but 26.4 is selected."
Fix: Auto-fixable command provided
```

## Error Code

- **E2224**: Xcode version mismatch
  - Severity: Error (development blocker)
  - Auto-fixable: When compatible version installed
  - Manual fix: Install matching Xcode from App Store

## Architecture

```
DoctorCommand (--fix flag)
    ↓
DoctorService.RunAllChecksAsync()
    ↓
AppleProvider.CheckHealth()
    ↓
XcodeCompatibilityChecker.CheckXcodeCompatibility()
    ↓
Returns HealthCheck with:
  - Status (Ok/Error/Skipped)
  - Message
  - Fix info (if applicable)
  - JSON details
```

## Test Coverage

| Component | Tests | Coverage |
|-----------|-------|----------|
| XcodeCompatibilityChecker | 10 | Version extraction, detection, structure |
| DoctorService | 7 new | Fix application, command parsing, integration |
| AppleProvider | 1 integration | Check inclusion, placement in health checks |
| Manual Tests | 4 scenarios | Direct use, provider, service, fix app |

## What Gets Checked

1. **Platform**: Returns Skipped on non-macOS
2. **Xcode**: Returns Skipped if Xcode not found
3. **SDK Packs**: Scans `/usr/local/share/dotnet/packs/`
4. **Requirements**: Parses MSBuild properties
5. **Compatibility**: Compares versions
6. **Available Fixes**: Checks for compatible Xcode installations

## Known Limitations

1. macOS only (graceful skip on other platforms)
2. Requires MSBuild properties in SDK packs
3. Version comparison is major.minor (ignores patch)
4. Detects only locally installed Xcode

## Next Steps

1. ✅ Review implementation
2. ✅ Run unit tests
3. ✅ Manual test on macOS
4. ⏳ CLI integration test
5. ⏳ Documentation update
6. ⏳ PR submission

## Code Quality

- ✅ Full XML documentation
- ✅ Comprehensive error handling
- ✅ No external dependencies
- ✅ Follows repo conventions
- ✅ Production-ready
- ✅ ~280 lines of production code
- ✅ ~160 lines of test code

## Support

For questions, see:
- IMPLEMENTATION_SUMMARY.md - Overview
- XCODE_COMPATIBILITY_CHECK_IMPLEMENTATION.md - Technical details
- Source code - Full documentation in XML comments
