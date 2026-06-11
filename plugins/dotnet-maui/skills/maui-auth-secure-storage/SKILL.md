---
name: maui-auth-secure-storage
description: >-
  Implement .NET MAUI authentication and secure storage flows using
  WebAuthenticator, MSAL.NET, platform callback URIs, token caching,
  SecureStorage, broker considerations, and Blazor Hybrid auth handoff. USE FOR:
  login/logout, OAuth/OIDC redirects, Microsoft Entra ID, callback URI setup,
  Android/iOS/Mac Catalyst/Windows auth configuration, secure token or secret
  storage, and native-to-Blazor auth state. DO NOT USE FOR: general app
  architecture (use maui-app-architecture), API client retry/offline behavior
  (use maui-networking-offline-data), or runtime UI debugging (use
  maui-devflow-debug).
---

# MAUI Auth and Secure Storage

Use this skill when a MAUI app needs sign-in, token handling, secure local
secrets, or auth state shared between native pages and Blazor Hybrid UI.

## Workflow

1. Inspect target frameworks, package references, `MauiProgram.cs`, platform
   manifests/plists, and existing auth abstractions.
2. Choose the auth primitive:
   - Use `WebAuthenticator` for provider-neutral OAuth/OIDC browser redirects.
   - Use MSAL.NET for Microsoft Entra ID, account selection, silent token
     acquisition, and broker integration.
3. Register redirect URIs in both the identity provider and platform app
   configuration. The scheme/host must match exactly.
4. Keep auth behind an injected service such as `IAuthService`; do not put token
   acquisition logic directly in pages or ViewModels.
5. Let MSAL own its token cache when using MSAL. Use `SecureStorage` for
   app-owned secrets, small encrypted values, and non-MSAL providers only when
   the provider requires app-managed refresh token storage.
6. Handle cancellation, denied consent, and expired sessions as first-class UI
   states instead of treating all auth failures as crashes.
7. For Blazor Hybrid, hand native auth state into scoped services or an
   `AuthenticationStateProvider`; do not rely on browser cookies or local
   storage as the source of truth.

## WebAuthenticator Pattern

```csharp
var result = await WebAuthenticator.Default.AuthenticateAsync(
    new WebAuthenticatorOptions
    {
        Url = authorizeUri,
        CallbackUrl = new Uri("myapp://auth")
    });

if (result.Properties.TryGetValue("error", out var error))
{
    throw new InvalidOperationException($"Authentication failed: {error}");
}

result.Properties.TryGetValue("code", out var code);
```

Build the authorize URI with PKCE when the provider supports it. Exchange the
authorization code through a secure backend or a provider-supported public
client flow; do not embed client secrets in the app package.

## MSAL.NET Pattern

```csharp
builder.Services.AddSingleton<IAuthService, MsalAuthService>();
```

For MSAL:

- Configure redirect URIs in the Entra app registration and the platform app.
- Prefer `AcquireTokenSilent` before interactive auth.
- Configure persistent token cache storage. MSAL's default cache is in-memory
  for many app scenarios; use the MSAL cache extension or token cache callbacks
  backed by secure platform storage so silent auth survives cold start.
- Use interactive auth only when there is no cached account, consent is needed,
  or silent acquisition returns a UI-required result.
- Inspect `MsalUiRequiredException.Classification`, error codes, and claims
  before blindly retrying interactive auth. Conditional Access, compliant-device,
  or Intune protection-policy failures may require compliance UX or Intune MAM
  integration, not another generic prompt.
- Opt into brokers only when the app registration, redirect URI, and platform
  setup are broker-ready.
- Store account identifiers or display names if needed; do not duplicate MSAL
  access or refresh tokens into `Preferences`.

## Platform Redirect Checklist

| Platform | Check |
| --- | --- |
| Android | For `WebAuthenticator`, add an Android activity subclass that inherits `Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity` and has an `IntentFilter` for the callback scheme/host. Use MAUI namespaces, not Xamarin.Auth or Xamarin.Essentials callback types. For MSAL broker flows, use the broker-compatible redirect URI and signature hash expected by the app registration. |
| iOS/Mac Catalyst | Add `CFBundleURLTypes` for the callback scheme. For broker flows, include required query schemes and redirect URI configuration from MSAL docs. |
| Windows | Register the custom protocol in the package manifest or app identity configuration used by the target. |

## SecureStorage Guardrails

- Use `SecureStorage.Default.GetAsync`, `SetAsync`, and `Remove` for small
  secrets only.
- Treat missing values as normal after reinstall, backup restore, device lock
  changes, or secure store reset.
- On Mac Catalyst, configure Keychain Sharing in
  `Platforms/MacCatalyst/Entitlements.plist`; secure storage calls fail without
  the required keychain entitlement.
- On iOS and Mac Catalyst, app extensions cannot read the host app's secure
  values unless a shared keychain access group is configured in both host and
  extension entitlements.
- Store expiration metadata with app-owned tokens and refresh before use.
- Prefer a backend token exchange when a provider requires confidential client
  secrets.
- Never log tokens, authorization codes, `id_token` values, refresh tokens, or
  full callback URLs.

## Blazor Hybrid Auth Handoff

- Register the native auth/session service in MAUI DI and consume it from Razor
  components through DI.
- Implement a custom `AuthenticationStateProvider` when Razor components need
  `[Authorize]` or `AuthorizeView`.
- Attach bearer tokens through a typed `HttpClient` handler that asks the native
  auth service for a fresh access token.
- Clear both native session state and Blazor auth state on logout.

## Validation Checklist

- Redirect URI values match across provider registration and platform files.
- Auth flows use PKCE or MSAL public-client patterns and contain no client
  secrets.
- Silent token acquisition is attempted before interactive MSAL prompts.
- Secure values are stored only in `SecureStorage` or the library-owned cache.
- Blazor Hybrid components receive auth state through DI, not browser-only
  storage.
