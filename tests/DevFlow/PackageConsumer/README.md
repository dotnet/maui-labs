# DevFlow Testing package consumer smoke

This project deliberately has a `PackageReference` to `Microsoft.Maui.DevFlow.Testing` and no
`ProjectReference`. `Validate-TestingPackage.ps1` copies the packed Testing and Driver packages
to an artifact-local feed, restores from that feed, and compiles the public surface.

The declared compile matrix is `net9.0`, `net10.0`, Android, iOS, Mac Catalyst, Windows, and
experimental AppKit (`net10.0-macos`). It is compile coverage only: it never launches an app,
connects to a device, or reports platform qualification. The script compiles only host/workload
targets that are available and records skipped targets with their reason. Official macOS publish
validation requires the Apple compile targets; local Windows validation requires Android and
Windows when their workloads are installed.

Run after packing DevFlow with both the Testing and Driver packages:

```powershell
pwsh ./tests/DevFlow/PackageConsumer/Validate-TestingPackage.ps1 `
  -PackageDirectory ./artifacts/packages
```
