using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Turns assistant <see cref="TextContent"/> into a strongly-typed <see cref="GardenFormattedTextBlock"/>.
/// Registered with <c>options.AddBlockHandler(new GardenFormattedTextHandler())</c>.
/// <para>
/// User-registered handlers run before the built-in text handler, so this claims assistant text first.
/// It intentionally passes on non-assistant text, letting the built-in handler render user messages as a
/// plain <see cref="TextContentBlock"/>.
/// </para>
/// </summary>
public sealed class GardenFormattedTextHandler : ContentBlockHandler<GardenFormattedTextBlock>
{
    public override BlockMappingResult<GardenFormattedTextBlock> Handle(
        BlockMappingContext context, GardenFormattedTextBlock state)
    {
        // Only format assistant text; user text falls through to the built-in handler.
        if (context.Update.Role != ChatRole.Assistant)
        {
            return state.Id != string.Empty
                ? BlockMappingResult<GardenFormattedTextBlock>.Complete()
                : BlockMappingResult<GardenFormattedTextBlock>.Pass();
        }

        TextContent? textContent = null;
        foreach (var content in context.UnhandledContents)
        {
            if (content is TextContent tc)
            {
                textContent = tc;
                break;
            }
        }

        if (textContent is null)
        {
            return state.Id != string.Empty
                ? BlockMappingResult<GardenFormattedTextBlock>.Complete()
                : BlockMappingResult<GardenFormattedTextBlock>.Pass();
        }

        context.MarkHandled(textContent);
        state.AppendText(textContent.Text ?? string.Empty);

        if (state.Id == string.Empty)
        {
            state.Id = context.Update.MessageId ?? Guid.NewGuid().ToString("N");
            return BlockMappingResult<GardenFormattedTextBlock>.Emit(state, state);
        }

        return BlockMappingResult<GardenFormattedTextBlock>.Update(state);
    }
}
