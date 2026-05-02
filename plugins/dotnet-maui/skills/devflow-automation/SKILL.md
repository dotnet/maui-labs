---
name: devflow-automation
description: >-
  Automate .NET MAUI app state via explicitly registered DevFlow Actions. USE
  FOR: discovering and invoking [DevFlowAction] shortcuts, logging in test
  users, seeding data, navigating to deep screens, bypassing long UI flows to
  reach target state quickly. DO NOT USE FOR: calling arbitrary methods,
  invoking DI services or framework types, basic UI interaction (tap/fill/scroll
  - use DevFlow MCP tools directly), visual tree inspection, screenshot capture,
  connectivity issues, or build/deployment problems.
---

# DevFlow Automation - Actions

DevFlow Actions are named shortcuts that a .NET MAUI app explicitly exposes for automation with `[DevFlowAction]`. Use them to reach useful app states quickly, such as logging in a test user, seeding data, toggling a feature flag, or navigating to a deep screen.

Actions are opt-in. DevFlow does not expose arbitrary reflection invoke; if you need a new shortcut, add an attributed method in app debug/test code, let Hot Reload apply it, then list and invoke the action.

## Start by Listing Actions

Always check for available actions early in a DevFlow session:

```
maui_list_actions
```

Look for action names and descriptions that match your goal. Common patterns:

- `login-*` for authentication shortcuts
- `seed-*` for data population
- `navigate-*` for deep links or screen setup
- `set-*` for feature flags or configuration
- `reset-*` for state cleanup

## Invoke an Action

Arguments are passed as a JSON array in parameter order. Omit trailing optional parameters to use their defaults.

```
maui_invoke_action actionName="login-test-user"
maui_invoke_action actionName="login-test-user" argsJson='["alice@test.com", "secret"]'
maui_invoke_action actionName="seed-catalog" argsJson='[100]'
```

After invoking an action, verify the state with a screenshot, tree query, or other DevFlow tools:

```
maui_screenshot
```

## Hot Reload Workflow

If no useful action exists and you can edit the app:

1. Add a public static method annotated with `[DevFlowAction]`.
2. Add `[Description]` to each parameter so agents know what to pass.
3. Save and let C# Hot Reload apply the change.
4. Call `maui_list_actions` again.
5. Invoke the new action with `maui_invoke_action`.

Example:

```csharp
using System.ComponentModel;
using Microsoft.Maui.DevFlow.Agent.Core;

public static class DebugHelpers
{
    [DevFlowAction("login-test-user", Description = "Log in as the standard test account")]
    public static async Task LoginTestUser(
        [Description("Email address for the test account")] string email = "test@example.com",
        [Description("Password for the test account")] string password = "password123")
    {
        await AuthService.LoginAsync(email, password);
    }
}
```

## Supported Parameter Types

Arguments are converted from JSON to these action parameter types:

| Type | JSON example |
|------|--------------|
| `string` | `"hello"` |
| `bool` | `true` or `false` |
| `int`, `long`, `short`, `byte` | `42` |
| `float`, `double`, `decimal` | `3.14` |
| `enum` | `"MemberName"` (case-insensitive) |
| arrays and supported list interfaces | `["a", "b"]` or `[1, 2, 3]` |
| nullable types | `null` or the value |

## Batch Support

Use `invoke-action` in batches when setup needs several steps:

```json
{
  "actions": [
    { "action": "invoke-action", "name": "login-test-user" },
    { "action": "invoke-action", "name": "seed-catalog", "args": [100] },
    { "action": "tap", "elementId": "btn-advanced" }
  ]
}
```

## Rules for App Developers

- Methods must be `public static`.
- Parameters should be simple supported types, enums, nullable supported types, arrays, or supported list interfaces.
- Add `[Description]` to parameters so AI agents know what to pass.
- Prefer returning `void`, `Task`, `ValueTask`, `Task<T>`, or `ValueTask<T>` with simple return values.
- Action names should be unique and intention-revealing.

The DevFlow analyzer validates attributed methods:

| Diagnostic | Severity | Description |
|------------|----------|-------------|
| MAUI_DFA001 | Error | Unsupported parameter type |
| MAUI_DFA002 | Error | Method must be public static |
| MAUI_DFA003 | Warning | Return type may not serialize cleanly |
| MAUI_DFA004 | Info | Missing `[Description]` on parameter |
| MAUI_DFA005 | Warning | Duplicate `[DevFlowAction]` name |

## Common Patterns

### Authentication Bypass

```
maui_list_actions
maui_invoke_action actionName="login-test-user"
maui_screenshot
```

### Data Seeding

```
maui_invoke_action actionName="seed-catalog" argsJson='[200]'
maui_invoke_action actionName="seed-orders" argsJson='[50, true]'
```

### Feature Flag Override

```
maui_invoke_action actionName="set-feature-flag" argsJson='["dark-mode", true]'
maui_invoke_action actionName="set-feature-flag" argsJson='["experimental-ui", true]'
```

### Navigate to a Deep Screen

```
maui_invoke_action actionName="navigate-to" argsJson='["//settings/advanced/network"]'
```
