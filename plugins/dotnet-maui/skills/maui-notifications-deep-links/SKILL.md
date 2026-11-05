---
name: maui-notifications-deep-links
description: >-
  Implement MAUI notifications and deep links. USE FOR: local/push notifications, FCM HTTP v1 migration, APNs/Azure Notification Hubs, permission UX, Android channels/`POST_NOTIFICATIONS`, iOS `UNUserNotificationCenter`, token lifecycle, notification tap navigation, app links/universal links/URI schemes. DO NOT USE FOR: OAuth callbacks, generic Shell navigation, picker/location permissions.
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
4. For migrated push-token guidance, explicitly say Android backends use the
   official **FCM HTTP v1** / **Firebase Cloud Messaging v1** API with
   service-account OAuth credentials, not legacy server keys. Treat OAuth 2.0
   as the credential flow, not the API version. iOS uses APNs device tokens.
5. Register refreshed device push tokens/installations with the backend or
   Azure Notification Hubs after sign-in, and disassociate them on logout.
6. Treat Azure Notification Hubs as optional infrastructure unless the app needs
   brokered installations, tags, templates, or multi-platform fanout.
7. Keep push payloads free of PII, access tokens, and confidential content; send
   route/entity IDs and fetch sensitive data after the app opens.
8. Normalize all notification taps and deep links into one app navigation
   service.
9. Route links with Shell routes or an explicit navigation abstraction.
10. Add diagnostics for token registration, payload parsing, and cold-start versus
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
If notification permission is denied, surface an in-app reminders state, banner,
or inbox fallback and provide a Settings path instead of silently dropping the
schedule request.

## Push Notifications

When the prompt mentions migrating a legacy Firebase server key, legacy FCM API,
or Xamarin push registrations, include these required points:

1. Android senders move to the official **FCM HTTP v1** / **Firebase Cloud
   Messaging v1** API with service-account OAuth credentials. Do not recommend
   legacy server keys. Treat OAuth 2.0 service-account authentication as the
   credential flow, not the API version.
2. iOS uses APNs device tokens, with sandbox/production environment alignment.
3. Azure Notification Hubs is optional infrastructure, not required for direct
   FCM/APNs token registration.
4. Upload refreshed tokens/installations after sign-in, associate them with the
   authenticated user, and disassociate or unregister them on logout.
5. Keep push payloads free of PII, access tokens, and confidential data; send
   route/entity IDs and fetch sensitive content after the app opens.

- Android uses Firebase Cloud Messaging (FCM); use the official **FCM HTTP v1**
  API on the backend. The legacy FCM HTTP API was retired in June 2024 and
  should not be used for migrated Xamarin or new MAUI apps.
- iOS and Mac Catalyst use APNs.
- Windows packaged apps use Windows Notification Service (WNS) channel URIs.
- Small apps can register FCM/APNs/WNS tokens directly with their own backend.
  Azure Notification Hubs is optional and useful when the app needs a broker for
  tags, installations, or multi-platform push management.
- When using Azure Notification Hubs with FCM, configure **FCM HTTP v1**
  service-account credentials rather than legacy server keys.
- Migrating the backend send API from legacy FCM to FCM HTTP v1 does not require
  a new client registration-token format; keep normal token refresh handling
  because FCM can still rotate tokens.
- Upload the current push token/installation ID to the backend after it changes.
- Protect token registration endpoints with app user authentication. Associate
  tokens with the signed-in user only after auth completes, and remove or
  disassociate them on logout.
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
    events.AddAndroid(android =>
    {
        android.OnCreate((activity, _) =>
        {
            var uri = activity.Intent?.Data?.ToString();
            if (!string.IsNullOrEmpty(uri))
                DeepLinkRouter.Route(uri);
        });

        android.OnNewIntent((activity, intent) =>
        {
            var uri = intent?.Data?.ToString();
            if (!string.IsNullOrEmpty(uri))
                DeepLinkRouter.Route(uri);
        });
    });

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

For cold-start links or notification taps, queue the route until MAUI has built
the app navigation surface. Do not call `Shell.Current.GoToAsync` from Android
`OnCreate` before `Shell.Current` exists.
While a queued link is resolving, show a pending navigation/loading state; after
arrival, move focus to the destination heading or announce the destination with
`SemanticScreenReader.Announce`.

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
- Push migration guidance names FCM HTTP v1/service-account credentials, APNs,
  and Notification Hubs as optional infrastructure.
- Push tokens are uploaded, refreshed, and disassociated on logout.
- Notification payloads contain route/entity references, not PII, access tokens,
  or secrets.
- Deep links work for cold start and resume.
- Cold-start links expose a pending state and announce/focus the destination when
  routing completes.
- App link/universal link verification files match the app identifiers.
