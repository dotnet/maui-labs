using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Canvas;
using Microsoft.Maui.AI.GenerativeUI.Dsl;
using Microsoft.Maui.AI.GenerativeUI.Inflation;
using Microsoft.Maui.AI.GenerativeUI.Registry;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using CanvasState = Microsoft.Maui.AI.GenerativeUI.Canvas.CanvasState;

namespace Microsoft.Maui.AI.GenerativeUI.Tools;

/// <summary>
/// The AI-facing client-UI tools. They let a model render bespoke, data-bound UI into the canvas,
/// live-edit and read back a form, confirm destructive actions, and hand off to registered screens.
/// Canvas/form mutations marshal to the main thread. See
/// <c>docs/GenerativeUI/spec/appendix-ui-dsl.md</c>.
/// </summary>
public sealed class GenerativeUiTools(
    CanvasState canvas,
    GenUiInflator inflator,
    GenerativeUiRegistry registry,
    IServiceProvider services)
{
    [ExportAIFunction("render_ui")]
    [Description(
        "Render a UI-DSL document into the canvas so the user sees a bespoke view of the data. " +
        "Pass a JSON object with: 'schemaVersion' (1), 'ui' (the root node tree), optional 'data' " +
        "(object that one-way 'bind' paths resolve against), and optional 'form' (object seeding " +
        "editable Field/Entry values). Node types: Stack, Card, Scroll, Separator, Spacer, Label, " +
        "Image, Badge, Icon, Button, Field, Entry, List. A node is { \"type\", optional \"id\", " +
        "\"bind\" (dotted path into data), \"style\" (token or list), \"children\", and type props }. " +
        "For changeable lists use itemsBind plus one template child; static lists may pre-expand. " +
        "Buttons use \"intent\": \"submit\" (a form's save), \"action:<name>\", etc.")]
    public async Task<string> RenderUiAsync(
        [Description("The render_ui document object (schemaVersion + ui + optional data/form/meta).")] JsonObject document,
        CancellationToken cancellationToken = default)
    {
        UiDocument doc;
        try
        {
            doc = UiDocument.Parse(document);
        }
        catch (UiDocumentParseException ex)
        {
            return $"Error: could not render UI — {ex.Message}. Fix the document and call render_ui again.";
        }

        if (doc.SchemaVersion != UiDocument.CurrentSchemaVersion)
            return $"Error: unsupported schemaVersion {doc.SchemaVersion}; this app supports {UiDocument.CurrentSchemaVersion}.";

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Seed the persistent state graph (data + form both live in one observable tree).
                if (doc.Data is { } data)
                    UiObjectBuilder.Merge(canvas.StateRoot, data);
                if (doc.Form is { } form)
                    UiObjectBuilder.Merge(canvas.StateRoot, form);

                var view = inflator.Inflate(doc, canvas.StateRoot);
                canvas.SetView(view);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return $"Error: rendering failed — {ex.Message}.";
        }

        return "Rendered the UI into the canvas.";
    }

    [ExportAIFunction("set_field")]
    [Description(
        "Set one field in the active form/state to a value (drives requests like 'set the quantity to 3'). " +
        "The on-screen control updates immediately. Convenience for a single replace patch.")]
    public async Task<string> SetFieldAsync(
        [Description("The field key (matches a Field/Entry 'key').")] string key,
        [Description("The new value.")] string value,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "Error: 'key' is required.";

        await MainThread.InvokeOnMainThreadAsync(() => canvas.StateRoot[key].Value = value).ConfigureAwait(false);
        return $"Set {key} = {value}.";
    }

    [ExportAIFunction("get_state")]
    [Description(
        "Read the current canvas state graph as JSON (form values, and any data the UI is bound to). " +
        "Read this before patching so you use real paths, and to gather form values for a write_api call. " +
        "Optionally pass a JSON Pointer path (e.g. '/cart/items') to read just a subtree.")]
    public async Task<string> GetStateAsync(
        [Description("Optional JSON Pointer to a subtree (e.g. '/cart'). Omit for the whole state.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        return await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var node = ResolvePointer(canvas.StateRoot, path);
            return node is null ? "null" : UiObjectBuilder.ToJson(node)?.ToJsonString() ?? "null";
        }).ConfigureAwait(false);
    }

    [ExportAIFunction("set_state")]
    [Description(
        "Replace the canvas state graph (or a subtree) with a JSON snapshot. Use to seed or fully reset " +
        "the data a rendered view is bound to. For small changes prefer apply_patch instead.")]
    public async Task<string> SetStateAsync(
        [Description("The state object.")] JsonObject state,
        [Description("Optional JSON Pointer to the subtree to replace. Omit to replace the whole state.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        var element = JsonSerializer.SerializeToElement(state);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var target = string.IsNullOrEmpty(path) ? canvas.StateRoot : ResolvePointer(canvas.StateRoot, path);
            if (target is null)
                return;
            foreach (var (key, _) in target.Members.ToList())
                target.RemoveMember(key);
            target.Children.Clear();
            UiObjectBuilder.Populate(target, element);
        }).ConfigureAwait(false);
        return "State updated.";
    }

    [ExportAIFunction("apply_patch")]
    [Description(
        "Apply a JSON Patch (RFC 6902) to the canvas state graph so a bound view updates IN PLACE — do " +
        "NOT call render_ui for data changes. Pass a JSON array of operations, e.g. " +
        "[{\"op\":\"remove\",\"path\":\"/cart/items/2\"}] or " +
        "[{\"op\":\"replace\",\"path\":\"/cart/items/0/quantity\",\"value\":3}] or " +
        "[{\"op\":\"add\",\"path\":\"/cart/items/-\",\"value\":{\"sku\":\"pears\",\"name\":\"Pears\"}}]. " +
        "Read get_state first so your paths are correct.")]
    public async Task<string> ApplyPatchAsync(
        [Description("A JSON Patch array of {op, path, value?, from?} operations.")] JsonArray operations,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<UiStatePatcher.PatchOperation> ops;
        try
        {
            ops = UiStatePatcher.ParseOperations(operations.ToJsonString());
        }
        catch (Exception ex)
        {
            return $"Error: could not parse the patch — {ex.Message}.";
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => UiStatePatcher.Apply(canvas.StateRoot, ops)).ConfigureAwait(false);
        }
        catch (UiPatchException ex)
        {
            return $"Error: patch failed — {ex.Message}. Read get_state and retry with correct paths.";
        }

        return $"Applied {ops.Count} patch operation(s).";
    }

    private static UiObject? ResolvePointer(UiObject root, string? pointer)
    {
        if (string.IsNullOrEmpty(pointer) || pointer == "/")
            return root;
        if (pointer[0] != '/')
            return null;
        var node = root;
        foreach (var raw in pointer[1..].Split('/'))
        {
            var token = raw.Replace("~1", "/").Replace("~0", "~");
            if (int.TryParse(token, out var i) && node.Children.Count > 0)
            {
                if (i < 0 || i >= node.Children.Count)
                    return null;
                node = node.Children[i];
            }
            else if (node.HasMember(token))
            {
                node = node[token];
            }
            else
            {
                return null;
            }
        }
        return node;
    }

    [ExportAIFunction("show_confirm")]
    [Description(
        "Show a confirmation overlay before a destructive or important action (e.g. delete). Resolves " +
        "when the user taps the button or types 'yes'. After it is confirmed, proceed with the write.")]
    public async Task<string> ShowConfirmAsync(
        [Description("Short title, e.g. 'Delete product?'")] string title,
        [Description("Message explaining what will happen.")] string message,
        [Description("Confirm button label (default 'Yes').")] string? confirmLabel = null,
        [Description("Cancel button label (default 'Cancel').")] string? cancelLabel = null,
        CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
            canvas.ShowConfirm(title, message, confirmLabel, cancelLabel)).ConfigureAwait(false);
        return "Showing the confirmation. Wait for the user to confirm (button or 'yes') before writing.";
    }

    [ExportAIFunction("clear_ui")]
    [Description("Reset the canvas to the empty welcome state and clear the form. Use to start over.")]
    public async Task<string> ClearUiAsync(CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(canvas.Reset).ConfigureAwait(false);
        return "Cleared the canvas.";
    }

    [ExportAIFunction("present_screen")]
    [Description(
        "Hand the whole canvas off to a registered full screen (e.g. checkout, a report). Supply the " +
        "screen name and its declared inputs. The screen loads its own data. Use list_ui_capabilities " +
        "to see available screens.")]
    public async Task<string> PresentScreenAsync(
        [Description("The registered screen name.")] string screen,
        [Description("Optional object containing the screen's declared inputs.")] JsonObject? inputs = null,
        CancellationToken cancellationToken = default)
    {
        var reg = registry.GetScreen(screen);
        if (reg is null)
            return $"Error: no registered screen named '{screen}'. Call list_ui_capabilities to see options.";

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (ActivatorUtilities.CreateInstance(services, reg.ScreenType) is View view)
                    canvas.SetView(view);
                else
                    canvas.SetView(new Label { Text = $"Screen '{screen}' is not a View." });
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return $"Error: could not present screen '{screen}' — {ex.Message}.";
        }

        return $"Presented the {screen} screen.";
    }

    [ExportAIFunction("list_ui_capabilities")]
    [Description("List the registered UI styles, controls, and screens (names + descriptions) this app supports.")]
    public string ListUiCapabilities()
    {
        var catalog = registry.DescribeCatalog();
        return string.IsNullOrWhiteSpace(catalog) ? "No app-specific UI capabilities are registered." : catalog;
    }
}
