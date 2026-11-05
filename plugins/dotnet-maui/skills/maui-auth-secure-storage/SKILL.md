---
name: maui-auth-secure-storage
description: >-
  Implement MAUI auth and secure storage. USE FOR: WebAuthenticator/MSAL, OAuth/OIDC redirects, Entra ID, callback URIs, Android intent filters, `CFBundleURLTypes`, token cache cleanup, SecureStorage, logout, Blazor Hybrid auth handoff. DO NOT USE FOR: architecture, API retries/offline data, UI debugging.
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
- Build the public client with an explicit redirect URI, for example
  `.WithRedirectUri("msal{client-id}://auth")` when using the default MAUI MSAL
  public-client redirect pattern. Broker-capable apps must use the broker
  redirect URI expected by the platform and app registration, often referred to
  as the `BrokerRedirectUri` in MSAL setup guidance.
- Prefer `AcquireTokenSilent` before interactive auth.
- Use MSAL's platform token cache for MAUI mobile targets and verify persistence
  on each target platform. Add custom token-cache serialization only for
  desktop, unsupported, or intentionally custom cache scenarios.
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
- On logout, call `GetAccountsAsync()` and `RemoveAsync(account)` for cached
  MSAL accounts before clearing app-owned auth state, so the next
  `AcquireTokenSilent` cannot return the signed-out user's tokens.

## Platform Redirect Checklist

| Platform | Check |
| --- | --- |
| Android | For `WebAuthenticator`, add an Android activity subclass that inherits `Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity` and has an `IntentFilter` for the callback scheme/host. Use MAUI namespaces, not Xamarin.Auth or Xamarin.Essentials callback types. For MSAL broker flows, use the broker-compatible redirect URI and signature hash expected by the app registration. |
| iOS/Mac Catalyst | Add `CFBundleURLTypes` for the callback scheme. For MSAL broker flows, add `LSApplicationQueriesSchemes` entries such as `msauthv2` and `msauthv3` so MSAL can detect the broker, and match the redirect URI scheme configured in the app registration. |
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
- Clear MSAL accounts with `RemoveAsync`, app-owned `SecureStorage` values, and
  Blazor auth state on logout.

## Validation Checklist

- Redirect URI values match across provider registration and platform files.
- Auth flows use PKCE or MSAL public-client patterns and contain no client
  secrets.
- Silent token acquisition is attempted before interactive MSAL prompts.
- Logout clears MSAL cached accounts with `RemoveAsync` and invalidates Blazor
  auth state when used.
- Secure values are stored only in `SecureStorage` or the library-owned cache.
- Blazor Hybrid components receive auth state through DI, not browser-only
  storage.
