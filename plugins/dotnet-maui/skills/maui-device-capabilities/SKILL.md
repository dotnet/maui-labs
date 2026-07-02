---
name: maui-device-capabilities
description: >-
  Implement MAUI device features with permissions/declarations. USE FOR: camera/media, `MediaPicker.IsCaptureSupported`, `FilePicker.PickAsync` durable copy to `FileSystem.AppDataDirectory`, `GeolocationRequest`, `Permissions.LocationWhenInUse`, `UseMauiMaps`, Android `READ_MEDIA_IMAGES`/Photo Picker, `NSCameraUsageDescription`, denied/cancelled flows. DO NOT USE FOR: notifications/auth/layout.
---

# MAUI Device Capabilities

Use this skill when a MAUI app needs device services such as files, media,
camera, location, or maps. Always pair runtime API calls with platform
declarations and a user-friendly denied/cancelled path.

## Workflow

1. Inspect target platforms and existing files under `Platforms/Android`,
   `Platforms/iOS`, `Platforms/MacCatalyst`, and `Platforms/Windows`.
2. Identify the exact capability and minimum permission needed.
3. Add platform declarations before requesting runtime permissions.
4. Request permissions close to the feature that needs them, after explaining
   why the app needs access.
5. Treat cancellation as normal user choice.
6. Keep device APIs behind services if ViewModels or tests should not depend on
   MAUI static APIs directly.
7. Validate on every target platform because permission names and UX differ.

## Permission Pattern

```csharp
var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

if (status != PermissionStatus.Granted)
{
    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
}

if (status != PermissionStatus.Granted)
{
    PermissionDeniedMessage = "Location is off. Enable it in Settings to show nearby stores.";
    CanOpenSettings = true;
    SemanticScreenReader.Announce(PermissionDeniedMessage);
    return;
}
```

Do not repeatedly prompt after a user denies access. Show a feature-specific
empty/blocked state that screen readers can observe, and when appropriate guide
the user to app settings with an explicit button that calls
`AppInfo.ShowSettingsUI()`.

## Platform Declaration Map

| Capability | Android | iOS/Mac Catalyst |
| --- | --- | --- |
| Camera/media capture | `CAMERA` and media storage permissions only when needed by target SDK behavior | `NSCameraUsageDescription`, `NSMicrophoneUsageDescription`, `NSPhotoLibraryUsageDescription` as applicable |
| Gallery/media library access | On Android 12 and earlier, audit `READ_EXTERNAL_STORAGE`; on Android 13+ use granular `READ_MEDIA_IMAGES`, `READ_MEDIA_VIDEO`, or `READ_MEDIA_AUDIO` as applicable, or prefer the system photo picker when it satisfies the scenario | `NSPhotoLibraryUsageDescription` or limited-library APIs when reading the photo library |
| Geolocation | `ACCESS_COARSE_LOCATION`, `ACCESS_FINE_LOCATION`, and background location only for true background scenarios | `NSLocationWhenInUseUsageDescription`; add always/background keys only when the app truly needs them |
| File picking | Usually no broad storage permission for system picker flows | Document picker usually needs no photo/camera usage key unless media capture/library access is used |
| Maps | Platform API key/configuration and `UseMauiMaps()` when using MAUI Maps | Platform map entitlement/configuration as required by the map provider |

Use the narrowest platform declaration that matches the feature. Do not add
background location, broad storage, or microphone declarations "just in case".

## Picker and Sensor Guardrails

- `FilePicker.PickAsync` and `PickMultipleAsync` should handle null, empty
  results, cancellation, and `PermissionException`.
- `MediaPicker` should check `IsCaptureSupported` before camera capture.
- Copy picked file streams into app storage when the app needs durable access;
  picker-provided handles can be temporary.
- `Geolocation.GetLastKnownLocationAsync` can be stale. Use
  `GetLocationAsync` with a `GeolocationRequest` for current location.
- Choose `GeolocationRequest.DesiredAccuracy` deliberately. Use medium or low
  accuracy to conserve battery unless the feature truly requires GPS precision.
- Pass cancellation tokens to location requests where the UI can cancel or the
  page can disappear.
- Migrating apps should audit old Xamarin `READ_EXTERNAL_STORAGE` declarations
  because Android 13+ media permissions changed runtime behavior.

## Permission UX

- Ask at the moment of need, not on first launch.
- Explain the user benefit before invoking the platform prompt.
- Provide a useful fallback when access is denied.
- Make denied/blocked states visible and announced with
  `SemanticProperties.Description`, `SemanticScreenReader.Announce`, or a
  focused explanatory label.
- Avoid loops that immediately re-request permission after denial.
- Make error/cancelled states visible and testable with `AutomationId` when UI
  is affected.

## Validation Checklist

- Platform manifests/plists include only the declarations required by the
  feature.
- Runtime permission requests happen before capability APIs that require them.
- Denied and cancelled flows are handled without crashes.
- Denied permission UI is visible, accessible, and offers Settings when the
  platform requires manual re-enable.
- Picked files/media are copied when durable access is required.
- Location code handles stale last-known data, timeout, and cancellation.
