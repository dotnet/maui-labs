---
name: maui-aspire-client
description: >-
  Connect MAUI apps to Aspire-hosted services. USE FOR: `AddServiceDiscovery`, `HttpClient` setup, emulator or simulator base addresses, dev certificates, auth handoff, and fallback behavior when service discovery config is missing. DO NOT USE FOR: generic offline caching, OAuth redirect setup, or server-side Aspire modeling.
---

# MAUI Aspire Client

Use this skill when a MAUI app consumes APIs or services orchestrated by .NET
Aspire during local development or testing.

## Workflow

1. Inspect whether the solution has an Aspire AppHost and a MAUI client project.
2. Use Aspire service discovery when the MAUI app targets .NET 8 or later and
   the resolved endpoint configuration is actually available to the app at
   runtime.
3. Register service discovery and typed `HttpClient` clients in `MauiProgram.cs`.
4. Use service names such as `https+http://apiservice` only when the matching
   `Services__...` configuration is present.
5. Provide platform-reachable fallback base addresses for emulator, simulator,
   desktop, and physical-device launches.
6. Account for emulator/simulator networking if bypassing Aspire service
   discovery.
7. Keep auth, token handlers, and backend API clients in DI.
8. Validate from Android emulator, iOS simulator, and physical devices as
   applicable.

## Service Discovery Pattern

MAUI apps deployed to emulators, simulators, or physical devices are usually not
child processes of the Aspire AppHost, so the AppHost cannot automatically
inject service discovery environment variables into the device app. Service-name
URIs work only when endpoint configuration is supplied to the MAUI app through
configuration, generated settings, or another explicit launch/deploy step.

When your dev flow can launch the MAUI project from the AppHost, wire the API
reference there first and pass endpoint configuration to the app:

```csharp
var api = builder.AddProject<Projects.ApiService>("apiservice");
builder.AddProject<Projects.MauiClient>("mauiclient")
       .WithReference(api);
```

```csharp
builder.Services.AddServiceDiscovery();

builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    // Development only: use "https://" for production endpoints.
    client.BaseAddress = new Uri("https+http://apiservice");
})
.AddServiceDiscovery();
```

For many clients, configure defaults once:

```csharp
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddServiceDiscovery();
});
```

The `https+http` scheme expresses HTTPS preference with HTTP fallback according
to the service discovery scheme rules. It can silently downgrade to HTTP when
only HTTP is available, so reserve it for development environments where that is
acceptable. Use `https://` exclusively for production endpoints carrying tokens
or business data.

## Standalone and Device Fallbacks

For most mobile device and emulator runs, provide explicit environment
configuration:

| Runtime | Fallback guidance |
| --- | --- |
| Android emulator | Use `10.0.2.2` for a host-machine API when not using Aspire service discovery. |
| iOS simulator | `localhost` usually resolves to the Mac host. |
| Desktop targets | `localhost` resolves to the same machine. |
| Physical devices | Use a LAN hostname/IP, dev tunnel, reverse proxy, or deployed endpoint. |

Do not hard-code emulator-only addresses into production configuration.

## Dev Certificates and Cleartext

- Prefer HTTPS with trusted development certificates.
- If HTTP is needed for local development, add debug-only cleartext exceptions.
- Prefer installing/trusting the development certificate or using an explicit
  debug-only HTTP fallback over certificate-validation bypass code. Do not show
  `DangerousAcceptAnyServerCertificateValidator` snippets in generated app
  guidance.
- Document how the app is launched under AppHost versus direct device deploy.
- Avoid disabling certificate validation globally in production code.

## Authentication

- Reuse the app's auth service and typed `HttpClient` handler to attach tokens.
- Keep OAuth redirect URIs platform-specific; Aspire service discovery does not
  replace native auth callback setup.
- For local auth services, ensure emulator/simulator redirect and callback URLs
  match the identity provider configuration.

## Validation Checklist

- The MAUI app registers service discovery or generated service defaults.
- Typed clients use Aspire service names when launched under AppHost.
- Standalone fallback addresses are platform-aware and not production defaults.
- HTTPS/dev certificate or debug-only cleartext behavior is explicit.
- Auth and API clients are wired through DI rather than page-level code.
