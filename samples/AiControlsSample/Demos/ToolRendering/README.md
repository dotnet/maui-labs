# Tool Rendering

## Overview

Shows custom inline rendering of tool results within the chat conversation. A `WeatherResultTemplate` renders a rich weather card when the `get_current_weather` tool returns data, while other tools use the generic `FunctionResultTemplate`.

## Features Demonstrated

- Custom `FunctionResultTemplate` subclass filtered by `ToolName` for per-tool rendering
- `ContentContextView` subclass (`WeatherResultView`) that parses JSON from tool results
- Template priority ordering — the most specific template (`WeatherResultTemplate`) is listed before the generic `FunctionResultTemplate` so it wins for matching tool names
- DI-registered `ChatSession` with shared `SampleTools` (weather, calculate, random fact, app info)

## How to Use

1. Navigate to **Tool Rendering** from the app shell
2. Ask "What's the weather in Tokyo?" — observe the rich weather card with city, temperature, conditions, humidity, and wind
3. Ask "Calculate (42 * 3) + 7" — observe the generic function result rendering
4. Ask "Tell me a random fact" — another generic result display
5. Compare the visual difference between the custom weather card and generic tool results

## Expected Behavior

- Weather queries render as a styled card with emoji icons (☀️/🌧️/etc.), temperature in °C, humidity %, wind speed km/h, and feels-like temperature
- Non-weather tool results (calculate, random fact, app info) render using the default `FunctionResultTemplate`
- `FunctionCallTemplate` shows the tool invocation (name + arguments) inline before the result
- The `WeatherResultView` gracefully handles missing JSON properties by displaying "--"

## Key Code Patterns

- **Custom template selection** — `<local:WeatherResultTemplate ToolName="get_current_weather" ViewType="{x:Type local:WeatherResultView}" />` placed before the generic `FunctionResultTemplate` in the templates list (`ToolRenderingPage.xaml:16-17`)
- **JSON result parsing** — `WeatherResultView.RefreshFromContentContext()` uses `JsonDocument.Parse` to extract fields from `FunctionResultContent.Result` (`WeatherResultView.xaml.cs:15-40`)
- **Template class** — `WeatherResultTemplate` is a minimal subclass of `FunctionResultTemplate` with no additional logic; filtering is done via the `ToolName` property (`WeatherResultTemplate.cs`)
