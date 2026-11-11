# Microsoft.Maui.AI.Chat.Controls

> **Experimental:** APIs may change between preview releases.

XAML block templates for the [`Microsoft.Maui.AI.Chat`](https://www.nuget.org/packages/Microsoft.Maui.AI.Chat/)
engine. The engine turns a chat client's streamed responses into conversation turns and content blocks (text, tool
calls, approvals, reasoning, media, and your own `[ToolBlock]` cards), and every one of them is bindable. This
package picks the view that shows each block, so you can build a chat UI from a plain `CollectionView`:

- **`BlockTemplateSelector`**, a `DataTemplateSelector` for any list of blocks: the first `BlockTemplate` whose
  condition matches a block wins.
- **`BlockMatch`** conditions: a block's type, its tool, its role, and `All`, `Any`, and `Not` to combine them, with
  markup extensions for the common ones (`{chat:Is chat:TextContentBlock}`, `{chat:Tool calculate}`,
  `{chat:Role user}`). A new kind of condition is a new `BlockMatch` type.
- **`ViewTemplate`** (`{chat:ViewTemplate local:CalculatorCard}`), a `DataTemplate` that creates a view through the
  app's services.
- **`BlockView`**, a base view that updates itself when its block changes, once per dispatcher pass however many
  tokens streamed in.

The package draws no chat UI of its own. The ChatPlayground sample,
[`samples/AIExtensions.Sample.ChatPlayground`](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground),
builds a complete chat with it: a list of turns, a composer, tool cards and approvals, a calculator worksheet that
folds several tool calls into one card, and notes that the user and the model edit together.

## Install

```shell
dotnet add package Microsoft.Maui.AI.Chat.Controls --prerelease
```

It references `Microsoft.Maui.AI.Chat`. In XAML, one namespace covers both packages:

```xml
xmlns:chat="http://schemas.microsoft.com/dotnet/2026/maui/ai/chat"
```

## Quick start

A session (`AgentContext`) holds the conversation. Its `Turns`, and each turn's `RequestBlocks` (what the user sent)
and `ResponseBlocks` (the answer), are observable collections, so a `CollectionView` of turns with a
`BindableLayout` of blocks shows the conversation as it streams:

```csharp
public sealed class ChatViewModel(IChatClient chatClient)
{
    public AgentContext Session { get; } = new(new UIAgent(chatClient));

    public Task SendAsync(string text) => Session.SendMessageAsync(text);
}
```

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:chat="http://schemas.microsoft.com/dotnet/2026/maui/ai/chat"
             xmlns:local="clr-namespace:MyApp"
             x:Class="MyApp.ChatPage"
             x:DataType="local:ChatViewModel">
    <ContentPage.Resources>
        <chat:BlockTemplateSelector x:Key="Blocks">
            <!-- A card of your own for one tool, created through the app's services. -->
            <chat:BlockTemplate When="{chat:Tool get_weather}" Template="{chat:ViewTemplate local:WeatherCard}" />

            <!-- Text, which streams in: RawText raises PropertyChanged, so the binding follows it. -->
            <chat:BlockTemplate When="{chat:Is chat:RichContentBlock}">
                <DataTemplate x:DataType="chat:RichContentBlock">
                    <Label Text="{Binding RawText}" />
                </DataTemplate>
            </chat:BlockTemplate>

            <!-- Every other tool call. -->
            <chat:BlockTemplate When="{chat:Is chat:FunctionInvocationContentBlock}">
                <DataTemplate x:DataType="chat:FunctionInvocationContentBlock">
                    <Label Text="{Binding ToolName, StringFormat='Used {0}'}" FontSize="12" />
                </DataTemplate>
            </chat:BlockTemplate>
        </chat:BlockTemplateSelector>
    </ContentPage.Resources>

    <CollectionView ItemsSource="{Binding Session.Turns}">
        <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="chat:ConversationTurn">
                <VerticalStackLayout Padding="16,8" Spacing="8">
                    <VerticalStackLayout HorizontalOptions="End"
                                         BindableLayout.ItemsSource="{Binding RequestBlocks}"
                                         BindableLayout.ItemTemplateSelector="{StaticResource Blocks}" />
                    <VerticalStackLayout BindableLayout.ItemsSource="{Binding ResponseBlocks}"
                                         BindableLayout.ItemTemplateSelector="{StaticResource Blocks}" />
                </VerticalStackLayout>
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>
```

Drive the session from the UI thread, as you would any collection a view is bound to: the engine changes the turns
and blocks on the thread that calls it (`SendMessageAsync` continues on the caller's context), and raises its
events there.

## Match blocks

| Condition | XAML | Code | Matches |
|---|---|---|---|
| `BlockTypeMatch` | `{chat:Is chat:TextContentBlock}` | `BlockMatch.Is<TextContentBlock>()` | The type and derived types, including `[ToolBlock]` types |
| `ToolMatch` | `{chat:Tool calculate}` | `BlockMatch.Tool("calculate")` | The tool's calls, approvals, and UI actions (case-insensitive) |
| `RoleMatch` | `{chat:Role user}` | `BlockMatch.Role(ChatRole.User)` | Blocks with the role (case-insensitive) |
| `AllMatch` | `<chat:AllMatch>…</chat:AllMatch>` | `BlockMatch.All(…)` | Blocks that satisfy every condition |
| `AnyMatch` | `<chat:AnyMatch>…</chat:AnyMatch>` | `BlockMatch.Any(…)` | Blocks that satisfy one condition |
| `NotMatch` | `<chat:NotMatch>…</chat:NotMatch>` | `BlockMatch.Not(…)` | Blocks that don't satisfy the condition |
| | | `BlockMatch.When(block => …)` | Blocks the predicate accepts |

Combine them as elements. This template shows a tool's calls but not its approvals:

```xml
<chat:BlockTemplate Template="{chat:ViewTemplate local:CalculatorCard}">
    <chat:BlockTemplate.When>
        <chat:AllMatch>
            <chat:BlockTypeMatch Type="chat:FunctionInvocationContentBlock" />
            <chat:ToolMatch Name="calculate" />
        </chat:AllMatch>
    </chat:BlockTemplate.When>
</chat:BlockTemplate>
```

For any other condition, derive from `BlockMatch`. Selecting a template can run often, so keep `Matches` fast and
free of side effects:

```csharp
public sealed class JsonAnswerMatch : BlockMatch
{
    public override bool Matches(ContentBlock block) =>
        block is TextContentBlock { RawText: var text } && block.Role == ChatRole.Assistant &&
        text.TrimStart().StartsWith('{');
}
```

```xml
<chat:BlockTemplate Template="{chat:ViewTemplate local:JsonCard}">
    <chat:BlockTemplate.When>
        <local:JsonAnswerMatch />
    </chat:BlockTemplate.When>
</chat:BlockTemplate>
```

A list selects a block's template when it shows the block, and doesn't select again when the block changes, so in
a plain `CollectionView` or `BindableLayout` match on what a block is (its type, tool, and role) rather than on
content that streams in.

## Templates

- **First match wins.** A `BlockTemplateSelector` tries its templates in order: put the most specific first and a
  catch-all (a template without `When`) last. There are no priorities.
- **No match, no view.** A block no template matches, and a block whose matching template has no `Template`, get
  one shared empty template (an empty `ContentView`), because a list needs a template for every item. Add a
  catch-all template to show every block.
- **Stable templates.** The selector returns the templates it holds, never copies, so a `CollectionView`, which
  reuses item views per template, reuses yours. A `DataTemplateSelector` used as a `Template`, such as another
  `BlockTemplateSelector`, is evaluated against the block.
- **Views from the app's services.** `{chat:ViewTemplate local:WeatherCard}` (or `new ViewTemplate(typeof(WeatherCard))`)
  creates each view through `IPlatformApplication.Current.Services`, the services `MauiProgram` builds, like a page
  Shell navigation creates: a view registered there (`builder.Services.AddTransient<WeatherCard>()`) is resolved,
  and any other view gets its constructor's parameters from those services. Register views as transient, since
  every block needs its own view. Without app services (before the app starts), the view's public parameterless
  constructor creates it.

## Views and bindings

A view's `BindingContext` is its block, and stays the block while the response streams. Bindings follow the standard
.NET contract: a binding refreshes when the property it reads raises `PropertyChanged`. The engine's blocks raise
their properties (`RawText`, `ToolName`, `Result`, `Status`, `IsAwaitingInput`, …), and the engine raises them for
any other block when its handler updates it, so `{Binding RawText}` and `{Binding Result.Result}` stay live. Two
kinds of binding keep their first value:

- a binding to the block itself (`{Binding .}`, or a converter over the whole block), which no property change
  refreshes;
- a binding that hands a view an object or list the block changes in place (`ItemsSource="{Binding Rows}"`),
  because reading the property again returns the same instance. Make such a list an `ObservableCollection<T>`.

Put logic over the whole block in a property of the block, or in a `BlockView`:

```csharp
[ToolBlock("get_weather")]
public sealed partial class WeatherBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "city")]
    public string City { get; set; } = "";

    [ToolResult(Name = "temperature")]
    public double Temperature { get; set; }
}

public sealed class WeatherCard : BlockView<WeatherBlock>
{
    private readonly Label _label = new();

    public WeatherCard() => Content = _label;

    protected override void OnBlockChanged() =>
        _label.Text = Block is { HasResult: true } weather
            ? $"{weather.City}: {weather.Temperature}°"
            : "Checking the weather…";
}
```

```xml
<chat:BlockTemplate When="{chat:Is local:WeatherBlock}" Template="{chat:ViewTemplate local:WeatherCard}" />
```

- `Block` is the binding context when it is a block (`BlockView<TBlock>` types it, and gives `null` for any other
  type).
- `OnBlockChanged()` runs when the view gets its block (or loses it), and after the block raises
  `PropertyChanged`: once for all the changes raised before the view's dispatcher runs it, however many tokens
  streamed in. Override `OnBlockPropertyChanged(string?)` to see each change as it is raised, on the thread that
  raised it.
- The block holds the view only weakly, like a binding, so a session that outlives a page doesn't keep its views.
- Generated `[ToolBlock]` blocks need `options.AddGeneratedToolBlocks()` on the agent.
- In a template for a `ToolApprovalBlock` or `UIActionBlock`, bind the buttons to `IsAwaitingInput`
  (`IsVisible="{Binding IsAwaitingInput}"`): it turns false once the block is answered, while the action runs, and
  when the turn stops, fails, or is restored, after which answers are ignored.

### Combine tool calls into one card

To show several tool calls of a response as one card, write a block that holds them and a handler that folds the
calls and their results into it. The first call emits the block; later calls and results update it in place.

```csharp
public sealed class WeatherComparisonBlock : ContentBlock
{
    public List<(FunctionCallContent Call, FunctionResultContent? Result)> Rows { get; } = [];
}

public sealed class WeatherComparisonHandler : ContentBlockHandler<WeatherComparisonBlock>
{
    public override BlockMappingResult<WeatherComparisonBlock> Handle(
        BlockMappingContext context, WeatherComparisonBlock block)
    {
        var changed = false;
        foreach (var content in context.UnhandledContents)
        {
            if (content is FunctionCallContent { Name: "get_weather" } call)
            {
                context.MarkHandled(call);
                block.Rows.Add((call, null));
                changed = true;
            }
            else if (content is FunctionResultContent result &&
                block.Rows.FindIndex(row => row.Call.CallId == result.CallId) is >= 0 and var index)
            {
                context.MarkHandled(result); // a custom block claims its results itself
                block.Rows[index] = (block.Rows[index].Call, result);
                changed = true;
            }
        }

        if (!changed)
            return BlockMappingResult<WeatherComparisonBlock>.Pass();
        if (string.IsNullOrEmpty(block.Id))
        {
            block.Id = "weather-" + context.Update.MessageId;
            return BlockMappingResult<WeatherComparisonBlock>.Emit(block, block);
        }

        return BlockMappingResult<WeatherComparisonBlock>.Update(block);
    }
}

var agent = new UIAgent(chatClient, options => options.AddBlockHandler(new WeatherComparisonHandler()));
```

```xml
<chat:BlockTemplate When="{chat:Is local:WeatherComparisonBlock}"
                    Template="{chat:ViewTemplate local:WeatherComparisonView}" />
```

`WeatherComparisonView` derives from `BlockView` and redraws the rows in `OnBlockChanged`, which runs after the
handler updates the block. `Rows` is a plain `List<T>` that the handler changes in place, which no binding can see;
to bind the rows instead (`BindableLayout.ItemsSource="{Binding Rows}"`), make `Rows` an
`ObservableCollection<T>`, which raises `CollectionChanged` for each row added or replaced. Mark the results handled
as well as the calls: a custom block is not one of the engine's function blocks, so an unclaimed result is dropped.
With `ChatOptions.AllowMultipleToolCalls = false` the card fills in one row at a time; with parallel calls, a
batch's results arrive together. The ChatPlayground sample's calculator worksheet works this way: an
`ActivityHandler` folds every `calculate` call of a response into one worksheet, with a row per calculation.

## Requirements

- .NET 10 and .NET MAUI 10 (`net10.0`; the package runs on every platform MAUI supports).
- `Microsoft.Maui.AI.Chat`, referenced by this package.

## Related

- [`Microsoft.Maui.AI.Chat`](https://www.nuget.org/packages/Microsoft.Maui.AI.Chat/): the engine (sessions, turns,
  blocks, tool approvals, UI actions, state).
- [`samples/AIExtensions.Sample.ChatPlayground`](https://github.com/dotnet/maui-labs/tree/main/samples/AIExtensions.Sample.ChatPlayground):
  a MAUI app whose chat uses both packages with real models.
