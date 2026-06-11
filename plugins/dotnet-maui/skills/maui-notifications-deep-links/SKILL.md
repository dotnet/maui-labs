---
name: maui-notifications-deep-links
description: >-
  Implement .NET MAUI local notifications, push notifications, FCM/APNs/Azure
  Notification Hubs registration, app links, universal links, custom URI
  schemes, token handling, and deep-link troubleshooting. USE FOR: notification
  permission UX, Android notification channels, iOS notification registration,
  push token upload, notification tap navigation, verified links, and callback
  routing. DO NOT USE FOR: OAuth callback-only flows (use
  maui-auth-secure-storage), generic Shell navigation (use maui-app-architecture),
  or device picker/location permissions (use maui-device-capabilities).
---

# MAUI Notifications and Deep Links

Use this skill when a MAUI app needs scheduled local alerts, remote push
notifications, or URLs that open the app and navigate to content.

## Workflow

1. Separate the entry points: local notification scheduling, push token
   registration, notification tap handling, custom URI schemes, and verified web
   links.
2. Add platform declarations and entitlements before writing app logic.
3. Request notification permission at a useful moment and support a denied state.
4. Register device push tokens with the backend or Azure Notification Hubs.
5. Normalize all notification taps and deep links into one app navigation
   service.
6. Route links with Shell routes or an explicit navigation abstraction.
7. Add diagnostics for token registration, payload parsing, and cold-start versus
   resume handling.

## Local Notifications

.NET MAUI apps typically implement local notifications with platform services or
a vetted toolkit abstraction.

```csharp
public interface INotificationService
{
    Task RequestPermissionAsync(CancellationToken cancellationToken);
    Task ScheduleReminderAsync(string id, string title, string body, DateTimeOffset when);
    Task CancelAsync(string id);
}
```

| Platform | Required considerations |
| --- | --- |
| Android | Create a `NotificationChannel` for Android 8+, request `POST_NOTIFICATIONS` on Android 13+, and include stable channel IDs. |
| iOS/Mac Catalyst | Request notification authorization through UserNotifications and handle foreground presentation explicitly. |
| Windows | Use the Windows app notification APIs appropriate for the packaged target. |

Keep notification IDs stable when updates/cancellation are required. Store
scheduled notification metadata in app storage if it must survive app restart.

## Push Notifications

- Android uses Firebase Cloud Messaging (FCM); use the FCM HTTP v1 API on the
  backend. The legacy FCM HTTP API was retired in June 2024 and should not be
  used for migrated Xamarin or new MAUI apps.
- iOS and Mac Catalyst use APNs.
- Windows packaged apps use Windows Notification Service (WNS) channel URIs.
- Small apps can register FCM/APNs/WNS tokens directly with their own backend.
  Azure Notification Hubs is optional and useful when the app needs a broker for
  tags, installations, or multi-platform push management.
- When using Azure Notification Hubs with FCM, configure FCM v1 service account
  credentials rather than legacy server keys.
- Upload the current push token/installation ID to the backend after it changes.
- Associate tokens with the signed-in user only after auth completes, and remove
  or disassociate them on logout.
- Do not put secrets, access tokens, or sensitive content in push payloads.
- Include a stable route or entity ID in the payload, then fetch sensitive data
  after the app opens.

## Deep Link Patterns

| Link type | Use when | Platform setup |
| --- | --- | --- |
| Custom URI scheme | App-private callbacks or simple app opens such as `myapp://orders/42` | Android intent filter, iOS/Mac Catalyst `CFBundleURLTypes`, Windows protocol registration |
| Android App Links | Verified HTTPS links on Android | Intent filter with `autoVerify` and hosted `assetlinks.json` |
| iOS Universal Links | Verified HTTPS links on iOS/Mac Catalyst | Associated Domains entitlement and hosted `apple-app-site-association` |

Use one parser that accepts all launch sources:

- App launched cold from a URL.
- App resumed from background by a URL.
- User tapped a local notification.
- User tapped a push notification.

## Wiring Deep Link Interception

Route native launch callbacks into one link handler:

```csharp
builder.ConfigureLifecycleEvents(events =>
{
    events.AddAndroid(android => android.OnNewIntent((activity, intent) =>
    {
        var uri = intent?.Data?.ToString();
        if (!string.IsNullOrEmpty(uri))
            DeepLinkRouter.Route(uri);
    }));

    events.AddiOS(ios => ios.OpenUrl((app, url, options) =>
    {
        DeepLinkRouter.Route(url.AbsoluteString);
        return true;
    }));
});
```

Override `App.OnAppLinkRequestReceived(Uri uri)` for verified HTTPS app links
and universal links, then forward the URI to the same parser used by
notifications and custom schemes.

## Troubleshooting Checklist

- Confirm the app package ID/bundle ID matches the FCM/APNs/Azure registration.
- Confirm APNs sandbox versus production environment matches the build.
- Confirm Android notification channel IDs are created before posting.
- Confirm Android 13+ notification runtime permission is granted.
- Confirm app link/universal link association files are reachable without
  redirects that break platform verification.
- Confirm link payloads map to registered Shell routes or navigation commands.

## Validation Checklist

- Notification permissions and platform declarations are present and scoped.
- Push tokens are uploaded, refreshed, and disassociated on logout.
- Notification payloads contain route/entity references, not secrets.
- Deep links work for cold start and resume.
- App link/universal link verification files match the app identifiers.
