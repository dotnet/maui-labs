using System.ComponentModel;
using System.Text.Json;
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
        "For a List, pre-expand one child node per item. Prefer binding display text to 'data' over " +
        "inlining. Buttons use \"intent\": \"submit\" (a form's save), \"action:<name>\", etc.")]
    public async Task<string> RenderUiAsync(
        [Description("The render_ui document as a JSON object (schemaVersion + ui + optional data/form/meta).")] string document,
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
                var dataRoot = doc.Data is { } data ? UiObjectBuilder.Build(data) : new UiObject();
                if (doc.Form is { } form)
                    UiObjectBuilder.Merge(canvas.FormRoot, form);

                var view = inflator.Inflate(doc, dataRoot, canvas.FormRoot);
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
        "Set one field in the active form to a value (drives requests like 'set the quantity to 3'). " +
        "The on-screen Entry updates immediately. Use the Field/Entry 'key' as the field name.")]
    public async Task<string> SetFieldAsync(
        [Description("The form field key (matches a Field/Entry 'key').")] string key,
        [Description("The new value.")] string value,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "Error: 'key' is required.";

        await MainThread.InvokeOnMainThreadAsync(() => canvas.FormRoot[key].Value = value).ConfigureAwait(false);
        return $"Set {key} = {value}.";
    }

    [ExportAIFunction("get_state")]
    [Description(
        "Read the current form values as a JSON object (drives 'save for me' — call this, then send " +
        "the values to write_api). Reflects whatever the user or set_field has entered.")]
    public async Task<string> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var json = await MainThread.InvokeOnMainThreadAsync(() =>
            UiObjectBuilder.ToJson(canvas.FormRoot)?.ToJsonString() ?? "{}").ConfigureAwait(false);
        return json;
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
        [Description("Optional JSON object of the screen's declared inputs.")] string? inputs = null,
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
