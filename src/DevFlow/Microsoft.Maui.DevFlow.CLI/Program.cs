using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.DevFlow.CLI;

/// <summary>
/// CDP-oriented CLI for automating MAUI Blazor WebViews.
/// Commands mirror CDP domain/method patterns for familiarity.
/// </summary>
class Program
{
    private static Parser? _parser;
    [ThreadStatic] private static bool _errorOccurred;

    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Microsoft.Maui.DevFlow CLI - automate MAUI apps via Agent API and Blazor WebViews via CDP");
        
        // Global agent connection options (available on all commands and subcommands)
        var agentPortOption = new Option<int>(
            ["--agent-port", "-ap"],
            () => ResolveAgentPort(),
            "Agent HTTP port (auto-discovered via broker, .mauidevflow, or default 9223)");
        var agentHostOption = new Option<string>(
            ["--agent-host", "-ah"],
            () => "localhost",
            "Agent HTTP host");
        var platformOption = new Option<string>(
            ["--platform", "-p"],
            () => "maccatalyst",
            "Target platform (maccatalyst, android, ios, windows)");
        var jsonOption = new Option<bool>(
            "--json",
            () => false,
            "Output as JSON (auto-enabled when stdout is piped/redirected)");
        var noJsonOption = new Option<bool>(
            "--no-json",
            () => false,
            "Force human-readable output even when piped");

        rootCommand.AddGlobalOption(agentPortOption);
        rootCommand.AddGlobalOption(agentHostOption);
        rootCommand.AddGlobalOption(platformOption);
        rootCommand.AddGlobalOption(jsonOption);
        rootCommand.AddGlobalOption(noJsonOption);

        // ===== CDP commands (Blazor WebView) =====
        
        var cdpCommand = new Command("cdp", "Blazor WebView automation via Chrome DevTools Protocol");

        var webviewOption = new Option<string?>(
            ["--webview", "-w"],
            () => null,
            "Target WebView by index, AutomationId, or element ID (default: first WebView)");
        cdpCommand.AddGlobalOption(webviewOption);
        
        // Browser domain commands
        var browserCommand = new Command("Browser", "Browser domain commands");
        
        var getVersionCmd = new Command("getVersion", "Get browser version info");
        getVersionCmd.SetHandler(async (host, port, wv) => await BrowserGetVersionAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        browserCommand.Add(getVersionCmd);
        
        cdpCommand.Add(browserCommand);
        
        // Runtime domain commands  
        var runtimeCommand = new Command("Runtime", "Runtime domain commands");
        
        var evaluateArg = new Argument<string>("expression", "JavaScript expression");
        var evaluateCmd = new Command("evaluate", "Evaluate JavaScript expression") { evaluateArg };
        evaluateCmd.SetHandler(async (host, port, expr, wv) => await RuntimeEvaluateAsync(host, port, expr, wv), agentHostOption, agentPortOption, evaluateArg, webviewOption);
        runtimeCommand.Add(evaluateCmd);
        
        cdpCommand.Add(runtimeCommand);
        
        // DOM domain commands
        var domCommand = new Command("DOM", "DOM domain commands");
        
        var getDocumentCmd = new Command("getDocument", "Get document root node");
        getDocumentCmd.SetHandler(async (host, port, wv) => await DomGetDocumentAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        domCommand.Add(getDocumentCmd);
        
        var querySelectorArg = new Argument<string>("selector", "CSS selector");
        var querySelectorCmd = new Command("querySelector", "Find element by CSS selector") { querySelectorArg };
        querySelectorCmd.SetHandler(async (host, port, selector, wv) => await DomQuerySelectorAsync(host, port, selector, wv), agentHostOption, agentPortOption, querySelectorArg, webviewOption);
        domCommand.Add(querySelectorCmd);
        
        var querySelectorAllArg = new Argument<string>("selector", "CSS selector");
        var querySelectorAllCmd = new Command("querySelectorAll", "Find all elements by CSS selector") { querySelectorAllArg };
        querySelectorAllCmd.SetHandler(async (host, port, selector, wv) => await DomQuerySelectorAllAsync(host, port, selector, wv), agentHostOption, agentPortOption, querySelectorAllArg, webviewOption);
        domCommand.Add(querySelectorAllCmd);
        
        var getOuterHtmlArg = new Argument<string>("selector", "CSS selector");
        var getOuterHtmlCmd = new Command("getOuterHTML", "Get element HTML") { getOuterHtmlArg };
        getOuterHtmlCmd.SetHandler(async (host, port, selector, wv) => await DomGetOuterHtmlAsync(host, port, selector, wv), agentHostOption, agentPortOption, getOuterHtmlArg, webviewOption);
        domCommand.Add(getOuterHtmlCmd);
        
        cdpCommand.Add(domCommand);
        
        // Page domain commands
        var pageCommand = new Command("Page", "Page domain commands");
        
        var navigateArg = new Argument<string>("url", "URL to navigate to");
        var navigateCmd = new Command("navigate", "Navigate to URL") { navigateArg };
        navigateCmd.SetHandler(async (host, port, url, wv) => await PageNavigateAsync(host, port, url, wv), agentHostOption, agentPortOption, navigateArg, webviewOption);
        pageCommand.Add(navigateCmd);
        
        var reloadCmd = new Command("reload", "Reload page");
        reloadCmd.SetHandler(async (host, port, wv) => await PageReloadAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        pageCommand.Add(reloadCmd);
        
        var captureScreenshotCmd = new Command("captureScreenshot", "Capture page screenshot (base64)");
        captureScreenshotCmd.SetHandler(async (host, port, wv) => await PageCaptureScreenshotAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        pageCommand.Add(captureScreenshotCmd);
        
        cdpCommand.Add(pageCommand);
        
        // Input domain commands
        var inputCommand = new Command("Input", "Input domain commands");
        
        var clickSelectorArg = new Argument<string>("selector", "CSS selector of element to click");
        var dispatchClickCmd = new Command("dispatchClickEvent", "Click element by selector") { clickSelectorArg };
        dispatchClickCmd.SetHandler(async (host, port, selector, wv) => await InputDispatchClickAsync(host, port, selector, wv), agentHostOption, agentPortOption, clickSelectorArg, webviewOption);
        inputCommand.Add(dispatchClickCmd);
        
        var insertTextArg = new Argument<string>("text", "Text to insert");
        var insertTextCmd = new Command("insertText", "Insert text at cursor") { insertTextArg };
        insertTextCmd.SetHandler(async (host, port, text, wv) => await InputInsertTextAsync(host, port, text, wv), agentHostOption, agentPortOption, insertTextArg, webviewOption);
        inputCommand.Add(insertTextCmd);
        
        var fillSelectorArg = new Argument<string>("selector", "CSS selector");
        var fillTextArg = new Argument<string>("text", "Text to fill");
        var fillCmd = new Command("fill", "Fill form field with text") { fillSelectorArg, fillTextArg };
        fillCmd.SetHandler(async (host, port, selector, text, wv) => await InputFillAsync(host, port, selector, text, wv), agentHostOption, agentPortOption, fillSelectorArg, fillTextArg, webviewOption);
        inputCommand.Add(fillCmd);
        
        cdpCommand.Add(inputCommand);
        
        // Convenience commands
        var statusCmd = new Command("status", "Check CDP connection status");
        statusCmd.SetHandler(async (host, port, wv) => await CdpStatusAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        cdpCommand.Add(statusCmd);
        
        var snapshotCmd = new Command("snapshot", "Get simplified DOM snapshot with element refs");
        snapshotCmd.SetHandler(async (host, port, wv) => await SnapshotAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        cdpCommand.Add(snapshotCmd);

        var webviewsCmd = new Command("webviews", "List available CDP WebViews");
        webviewsCmd.SetHandler(async (host, port, json, noJson) => await CdpWebViewsAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson)), agentHostOption, agentPortOption, jsonOption, noJsonOption);
        cdpCommand.Add(webviewsCmd);

        var sourceCmd = new Command("source", "Get page HTML source from a WebView");
        sourceCmd.SetHandler(async (host, port, wv) => await CdpSourceAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        cdpCommand.Add(sourceCmd);
        
        rootCommand.Add(cdpCommand);
        
        // ===== MAUI Native commands =====

        var mauiCommand = new Command("MAUI", "Native MAUI app automation commands");

        // Shared window option for commands that target a specific window
        var windowOption = new Option<int?>("--window", "Window index (0-based, default: first window)");

        // MAUI status
        var mauiStatusCmd = new Command("status", "Check agent connection") { windowOption };
        mauiStatusCmd.SetHandler(async (host, port, json, noJson, window) => await MauiStatusAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson), window), agentHostOption, agentPortOption, jsonOption, noJsonOption, windowOption);
        mauiCommand.Add(mauiStatusCmd);

        // MAUI tree
        var treeDepthOption = new Option<int>("--depth", () => 0, "Max tree depth (0=unlimited)");
        var treeFieldsOption = new Option<string?>("--fields", "Comma-separated fields to include (e.g. id,type,text,automationId,bounds)");
        var treeFormatOption = new Option<string?>("--format", "Output format: compact (id,type,text,automationId,bounds only)");
        var mauiTreeCmd = new Command("tree", "Dump visual tree") { treeDepthOption, treeFieldsOption, treeFormatOption, windowOption };
        mauiTreeCmd.SetHandler(async (host, port, json, noJson, depth, window, fields, format) => await MauiTreeAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson), depth, window, fields, format), agentHostOption, agentPortOption, jsonOption, noJsonOption, treeDepthOption, windowOption, treeFieldsOption, treeFormatOption);
        mauiCommand.Add(mauiTreeCmd);

        // MAUI query
        var queryTypeOption = new Option<string?>("--type", "Filter by element type");
        var queryAutoIdOption = new Option<string?>("--automationId", "Filter by AutomationId");
        var queryTextOption = new Option<string?>("--text", "Filter by text content");
        var querySelectorOption = new Option<string?>("--selector", "CSS selector (e.g. 'Button:visible', 'StackLayout > Label[Text^=\"Hello\"]')");
        var queryFieldsOption = new Option<string?>("--fields", "Comma-separated fields to include (e.g. id,type,text,automationId,bounds)");
        var queryFormatOption = new Option<string?>("--format", "Output format: compact (id,type,text,automationId,bounds only)");
        var queryWaitUntilOption = new Option<string?>("--wait-until", "Wait condition: exists or gone");
        var queryTimeoutOption = new Option<int>("--timeout", () => 10, "Timeout in seconds for --wait-until");
        var mauiQueryCmd = new Command("query", "Find elements") { queryTypeOption, queryAutoIdOption, queryTextOption, querySelectorOption, queryFieldsOption, queryFormatOption, queryWaitUntilOption, queryTimeoutOption };
        mauiQueryCmd.SetHandler(async (ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(agentHostOption)!;
            var port = ctx.ParseResult.GetValueForOption(agentPortOption);
            var isJson = OutputWriter.ResolveJsonMode(
                ctx.ParseResult.GetValueForOption(jsonOption),
                ctx.ParseResult.GetValueForOption(noJsonOption));
            var type = ctx.ParseResult.GetValueForOption(queryTypeOption);
            var autoId = ctx.ParseResult.GetValueForOption(queryAutoIdOption);
            var text = ctx.ParseResult.GetValueForOption(queryTextOption);
            var selector = ctx.ParseResult.GetValueForOption(querySelectorOption);
            var fields = ctx.ParseResult.GetValueForOption(queryFieldsOption);
            var format = ctx.ParseResult.GetValueForOption(queryFormatOption);
            var waitUntil = ctx.ParseResult.GetValueForOption(queryWaitUntilOption);
            var timeout = ctx.ParseResult.GetValueForOption(queryTimeoutOption);
            await MauiQueryAsync(host, port, isJson, type, autoId, text, selector, fields, format, waitUntil, timeout);
        });
        mauiCommand.Add(mauiQueryCmd);

        // MAUI hittest
        var hitTestXArg = new Argument<double>("x", "X coordinate");
        var hitTestYArg = new Argument<double>("y", "Y coordinate");
        var mauiHitTestCmd = new Command("hittest", "Find elements at a point") { hitTestXArg, hitTestYArg, windowOption };
        mauiHitTestCmd.SetHandler(async (host, port, json, noJson, x, y, window) => await MauiHitTestAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson), x, y, window),
            agentHostOption, agentPortOption, jsonOption, noJsonOption, hitTestXArg, hitTestYArg, windowOption);
        mauiCommand.Add(mauiHitTestCmd);

        // Shared element resolution options (for tap, fill, clear, focus)
        var resolveAutoIdOption = new Option<string?>("--automationId", "Resolve element by AutomationId (instead of element ID)");
        var resolveTypeOption = new Option<string?>("--type", "Resolve element by type (used with --automationId or alone)");
        var resolveTextOption = new Option<string?>("--text", "Resolve element by text content");
        var resolveIndexOption = new Option<int>("--index", () => 0, "Index when multiple elements match (0-based, default: first)");
        var andScreenshotOption = new Option<string?>("--and-screenshot", "Take screenshot after action (optional: output path)");
        andScreenshotOption.Arity = ArgumentArity.ZeroOrOne;
        var andTreeOption = new Option<bool>("--and-tree", "Dump visual tree after action");
        var andTreeDepthOption = new Option<int>("--and-tree-depth", () => 2, "Max depth for --and-tree");

        // MAUI tap
        var tapIdArg = new Argument<string?>("elementId", () => null, "Element ID to tap (optional if --automationId, --type, or --text is used)");
        var mauiTapCmd = new Command("tap", "Tap element") { tapIdArg, resolveAutoIdOption, resolveTypeOption, resolveTextOption, resolveIndexOption, andScreenshotOption, andTreeOption, andTreeDepthOption };
        mauiTapCmd.SetHandler(async (ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(agentHostOption)!;
            var port = ctx.ParseResult.GetValueForOption(agentPortOption);
            var isJson = OutputWriter.ResolveJsonMode(ctx.ParseResult.GetValueForOption(jsonOption), ctx.ParseResult.GetValueForOption(noJsonOption));
            var id = ctx.ParseResult.GetValueForArgument(tapIdArg);
            var autoId = ctx.ParseResult.GetValueForOption(resolveAutoIdOption);
            var type = ctx.ParseResult.GetValueForOption(resolveTypeOption);
            var text = ctx.ParseResult.GetValueForOption(resolveTextOption);
            var index = ctx.ParseResult.GetValueForOption(resolveIndexOption);
            var andScreenshot = ctx.ParseResult.GetValueForOption(andScreenshotOption);
            var hasAndScreenshot = ctx.ParseResult.FindResultFor(andScreenshotOption) != null;
            var andTree = ctx.ParseResult.GetValueForOption(andTreeOption);
            var andTreeDepth = ctx.ParseResult.GetValueForOption(andTreeDepthOption);
            var resolvedId = await ResolveElementIdAsync(host, port, isJson, id, autoId, type, text, index);
            if (resolvedId == null) return;
            await MauiTapAsync(host, port, isJson, resolvedId);
            await HandlePostActionFlags(host, port, isJson, hasAndScreenshot, andScreenshot, andTree, andTreeDepth);
        });
        mauiCommand.Add(mauiTapCmd);

        // MAUI fill
        var fillIdArg = new Argument<string?>("elementId", () => null, "Element ID (optional if --automationId, --type, or --text is used)");
        var fillTextArg2 = new Argument<string>("text", "Text to fill");
        var mauiFillCmd = new Command("fill", "Fill text into element") { fillIdArg, fillTextArg2, resolveAutoIdOption, resolveTypeOption, resolveTextOption, resolveIndexOption, andScreenshotOption, andTreeOption, andTreeDepthOption };
        mauiFillCmd.SetHandler(async (ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(agentHostOption)!;
            var port = ctx.ParseResult.GetValueForOption(agentPortOption);
            var isJson = OutputWriter.ResolveJsonMode(ctx.ParseResult.GetValueForOption(jsonOption), ctx.ParseResult.GetValueForOption(noJsonOption));
            var id = ctx.ParseResult.GetValueForArgument(fillIdArg);
            var fillText = ctx.ParseResult.GetValueForArgument(fillTextArg2);
            var autoId = ctx.ParseResult.GetValueForOption(resolveAutoIdOption);
            var type = ctx.ParseResult.GetValueForOption(resolveTypeOption);
            var text = ctx.ParseResult.GetValueForOption(resolveTextOption);
            var index = ctx.ParseResult.GetValueForOption(resolveIndexOption);
            var andScreenshot = ctx.ParseResult.GetValueForOption(andScreenshotOption);
            var hasAndScreenshot = ctx.ParseResult.FindResultFor(andScreenshotOption) != null;
            var andTree = ctx.ParseResult.GetValueForOption(andTreeOption);
            var andTreeDepth = ctx.ParseResult.GetValueForOption(andTreeDepthOption);
            var resolvedId = await ResolveElementIdAsync(host, port, isJson, id, autoId, type, text, index);
            if (resolvedId == null) return;
            await MauiFillAsync(host, port, isJson, resolvedId, fillText);
            await HandlePostActionFlags(host, port, isJson, hasAndScreenshot, andScreenshot, andTree, andTreeDepth);
        });
        mauiCommand.Add(mauiFillCmd);

        // MAUI clear
        var clearIdArg = new Argument<string?>("elementId", () => null, "Element ID to clear (optional if --automationId, --type, or --text is used)");
        var mauiClearCmd = new Command("clear", "Clear text from element") { clearIdArg, resolveAutoIdOption, resolveTypeOption, resolveTextOption, resolveIndexOption, andScreenshotOption, andTreeOption, andTreeDepthOption };
        mauiClearCmd.SetHandler(async (ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(agentHostOption)!;
            var port = ctx.ParseResult.GetValueForOption(agentPortOption);
            var isJson = OutputWriter.ResolveJsonMode(ctx.ParseResult.GetValueForOption(jsonOption), ctx.ParseResult.GetValueForOption(noJsonOption));
            var id = ctx.ParseResult.GetValueForArgument(clearIdArg);
            var autoId = ctx.ParseResult.GetValueForOption(resolveAutoIdOption);
            var type = ctx.ParseResult.GetValueForOption(resolveTypeOption);
            var text = ctx.ParseResult.GetValueForOption(resolveTextOption);
            var index = ctx.ParseResult.GetValueForOption(resolveIndexOption);
            var andScreenshot = ctx.ParseResult.GetValueForOption(andScreenshotOption);
            var hasAndScreenshot = ctx.ParseResult.FindResultFor(andScreenshotOption) != null;
            var andTree = ctx.ParseResult.GetValueForOption(andTreeOption);
            var andTreeDepth = ctx.ParseResult.GetValueForOption(andTreeDepthOption);
            var resolvedId = await ResolveElementIdAsync(host, port, isJson, id, autoId, type, text, index);
            if (resolvedId == null) return;
            await MauiClearAsync(host, port, isJson, resolvedId);
            await HandlePostActionFlags(host, port, isJson, hasAndScreenshot, andScreenshot, andTree, andTreeDepth);
        });
        mauiCommand.Add(mauiClearCmd);

        // MAUI screenshot
        var screenshotOutputOption = new Option<string?>("--output", "Output file path");
        var screenshotIdOption = new Option<string?>("--id", "Element ID to capture");
        var screenshotSelectorOption = new Option<string?>("--selector", "CSS selector to capture (first match)");
        var screenshotOverwriteOption = new Option<bool>("--overwrite", () => false, "Overwrite existing file (default: fail if exists)");
        var screenshotMaxWidthOption = new Option<int?>("--max-width", "Resize screenshot to this max width (overrides auto-scaling)");
        var screenshotScaleOption = new Option<string?>("--scale", "Scale mode: 'native' keeps full HiDPI resolution, default auto-scales to 1x logical pixels");
        var mauiScreenshotCmd = new Command("screenshot", "Take screenshot") { screenshotOutputOption, windowOption, screenshotIdOption, screenshotSelectorOption, screenshotOverwriteOption, screenshotMaxWidthOption, screenshotScaleOption };
        mauiScreenshotCmd.SetHandler(async (ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(agentHostOption)!;
            var port = ctx.ParseResult.GetValueForOption(agentPortOption);
            var isJson = OutputWriter.ResolveJsonMode(ctx.ParseResult.GetValueForOption(jsonOption), ctx.ParseResult.GetValueForOption(noJsonOption));
            await MauiScreenshotAsync(host, port, isJson,
                ctx.ParseResult.GetValueForOption(screenshotOutputOption),
                ctx.ParseResult.GetValueForOption(windowOption),
                ctx.ParseResult.GetValueForOption(screenshotIdOption),
                ctx.ParseResult.GetValueForOption(screenshotSelectorOption),
                ctx.ParseResult.GetValueForOption(screenshotOverwriteOption),
                ctx.ParseResult.GetValueForOption(screenshotMaxWidthOption),
                ctx.ParseResult.GetValueForOption(screenshotScaleOption));
        });
        mauiCommand.Add(mauiScreenshotCmd);

        // MAUI recording subcommands
        var recordingCommand = new Command("recording", "Screen recording (start/stop/status)");

        var recordingOutputOption = new Option<string?>("--output", "Output file path");
        var recordingTimeoutOption = new Option<int>("--timeout", () => 30, "Max recording duration in seconds");
        var recordingStartCmd = new Command("start", "Start screen recording") { recordingOutputOption, recordingTimeoutOption };
        recordingStartCmd.SetHandler(async (host, port, platform, output, timeout) =>
            await RecordingStartAsync(host, port, platform, output, timeout),
            agentHostOption, agentPortOption, platformOption, recordingOutputOption, recordingTimeoutOption);
        recordingCommand.Add(recordingStartCmd);

        var recordingStopCmd = new Command("stop", "Stop active recording");
        recordingStopCmd.SetHandler(async (host, port, platform) =>
            await RecordingStopAsync(host, port, platform),
            agentHostOption, agentPortOption, platformOption);
        recordingCommand.Add(recordingStopCmd);

        var recordingStatusCmd = new Command("status", "Check if a recording is in progress");
        recordingStatusCmd.SetHandler(() => RecordingStatusAsync());
        recordingCommand.Add(recordingStatusCmd);

        mauiCommand.Add(recordingCommand);

        // MAUI property
        var propIdArg = new Argument<string>("elementId", "Element ID");
        var propNameArg = new Argument<string>("propertyName", "Property name");
        var mauiPropertyCmd = new Command("property", "Get element property") { propIdArg, propNameArg };
        mauiPropertyCmd.SetHandler(async (host, port, json, noJson, id, name) => await MauiPropertyAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson), id, name), agentHostOption, agentPortOption, jsonOption, noJsonOption, propIdArg, propNameArg);
        mauiCommand.Add(mauiPropertyCmd);

        // MAUI set-property
        var setPropIdArg = new Argument<string>("elementId", "Element ID");
        var setPropNameArg = new Argument<string>("propertyName", "Property name");
        var setPropValueArg = new Argument<string>("value", "Value to set");
        var mauiSetPropertyCmd = new Command("set-property", "Set element property (live editing)") { setPropIdArg, setPropNameArg, setPropValueArg };
        mauiSetPropertyCmd.SetHandler(async (host, port, json, noJson, id, name, value) => await MauiSetPropertyAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson), id, name, value), agentHostOption, agentPortOption, jsonOption, noJsonOption, setPropIdArg, setPropNameArg, setPropValueArg);
        mauiCommand.Add(mauiSetPropertyCmd);

        // MAUI element
        var elementIdArg = new Argument<string>("elementId", "Element ID");
        var mauiElementCmd = new Command("element", "Get element details") { elementIdArg };
        mauiElementCmd.SetHandler(async (host, port, json, noJson, id) => await MauiElementAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson), id), agentHostOption, agentPortOption, jsonOption, noJsonOption, elementIdArg);
        mauiCommand.Add(mauiElementCmd);

        // MAUI navigate (Shell)
        var navRouteArg = new Argument<string>("route", "Shell route (e.g. //blazor)");
        var mauiNavigateCmd = new Command("navigate", "Navigate to Shell route") { navRouteArg };
        mauiNavigateCmd.SetHandler(async (host, port, json, noJson, route) => await MauiNavigateAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson), route), agentHostOption, agentPortOption, jsonOption, noJsonOption, navRouteArg);
        mauiCommand.Add(mauiNavigateCmd);

        // MAUI scroll
        var scrollElementIdOption = new Option<string?>("--element", "Element ID to scroll into view or to scroll within");
        var scrollDeltaXOption = new Option<double>("--dx", () => 0, "Horizontal scroll delta (pixels)");
        var scrollDeltaYOption = new Option<double>("--dy", () => 0, "Vertical scroll delta (pixels, negative = down)");
        var scrollAnimatedOption = new Option<bool>("--animated", () => true, "Animate the scroll");
        var scrollItemIndexOption = new Option<int?>("--item-index", "Item index to scroll to (for CollectionView/ListView)");
        var scrollGroupIndexOption = new Option<int?>("--group-index", "Group index for grouped CollectionView");
        var scrollPositionOption = new Option<string?>("--position", "Scroll position: MakeVisible (default), Start, Center, End");
        var mauiScrollCmd = new Command("scroll", "Scroll content by delta, item index, or scroll element into view") { scrollElementIdOption, scrollDeltaXOption, scrollDeltaYOption, scrollAnimatedOption, scrollItemIndexOption, scrollGroupIndexOption, scrollPositionOption, windowOption };
        mauiScrollCmd.SetHandler(async (ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(agentHostOption)!;
            var port = ctx.ParseResult.GetValueForOption(agentPortOption);
            var isJson = OutputWriter.ResolveJsonMode(ctx.ParseResult.GetValueForOption(jsonOption), ctx.ParseResult.GetValueForOption(noJsonOption));
            await MauiScrollAsync(host, port, isJson,
                ctx.ParseResult.GetValueForOption(scrollElementIdOption),
                ctx.ParseResult.GetValueForOption(scrollDeltaXOption),
                ctx.ParseResult.GetValueForOption(scrollDeltaYOption),
                ctx.ParseResult.GetValueForOption(scrollAnimatedOption),
                ctx.ParseResult.GetValueForOption(windowOption),
                ctx.ParseResult.GetValueForOption(scrollItemIndexOption),
                ctx.ParseResult.GetValueForOption(scrollGroupIndexOption),
                ctx.ParseResult.GetValueForOption(scrollPositionOption));
        });
        mauiCommand.Add(mauiScrollCmd);

        // MAUI focus
        var focusIdArg = new Argument<string?>("elementId", () => null, "Element ID to focus (optional if --automationId, --type, or --text is used)");
        var mauiFocusCmd = new Command("focus", "Set focus to element") { focusIdArg, resolveAutoIdOption, resolveTypeOption, resolveTextOption, resolveIndexOption };
        mauiFocusCmd.SetHandler(async (ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(agentHostOption)!;
            var port = ctx.ParseResult.GetValueForOption(agentPortOption);
            var isJson = OutputWriter.ResolveJsonMode(ctx.ParseResult.GetValueForOption(jsonOption), ctx.ParseResult.GetValueForOption(noJsonOption));
            var id = ctx.ParseResult.GetValueForArgument(focusIdArg);
            var autoId = ctx.ParseResult.GetValueForOption(resolveAutoIdOption);
            var type = ctx.ParseResult.GetValueForOption(resolveTypeOption);
            var text = ctx.ParseResult.GetValueForOption(resolveTextOption);
            var index = ctx.ParseResult.GetValueForOption(resolveIndexOption);
            var resolvedId = await ResolveElementIdAsync(host, port, isJson, id, autoId, type, text, index);
            if (resolvedId == null) return;
            await MauiFocusAsync(host, port, isJson, resolvedId);
        });
        mauiCommand.Add(mauiFocusCmd);

        // MAUI resize
        var resizeWidthArg = new Argument<int>("width", "Window width");
        var resizeHeightArg = new Argument<int>("height", "Window height");
        var mauiResizeCmd = new Command("resize", "Resize app window") { resizeWidthArg, resizeHeightArg, windowOption };
        mauiResizeCmd.SetHandler(async (host, port, json, noJson, w, h, window) => await MauiResizeAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson), w, h, window), agentHostOption, agentPortOption, jsonOption, noJsonOption, resizeWidthArg, resizeHeightArg, windowOption);
        mauiCommand.Add(mauiResizeCmd);

        // MAUI alert subcommands — supports iOS simulator (apple CLI) and Mac Catalyst (macOS AX API)
        var alertCommand = new Command("alert", "Detect and dismiss system/app dialogs");

        // detect
        var detectUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var detectPid = new Option<int?>("--pid", "Mac Catalyst app PID (auto-detects if omitted)");
        var alertDetectCmd = new Command("detect", "Check if an alert/dialog is visible") { detectUdid, detectPid };
        alertDetectCmd.SetHandler(async (udid, pid, host, port, json, noJson) =>
            await AlertDetectAsync(udid, pid, host, port, OutputWriter.ResolveJsonMode(json, noJson)), detectUdid, detectPid, agentHostOption, agentPortOption, jsonOption, noJsonOption);
        alertCommand.Add(alertDetectCmd);

        // dismiss
        var dismissUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var dismissPid = new Option<int?>("--pid", "Mac Catalyst app PID (auto-detects if omitted)");
        var dismissButtonArg = new Argument<string?>("button", () => null, "Button label to tap (default: first accept-style button)");
        var alertDismissCmd = new Command("dismiss", "Dismiss the current alert/dialog") { dismissButtonArg, dismissUdid, dismissPid };
        alertDismissCmd.SetHandler(async (udid, pid, host, port, button, json, noJson) =>
            await AlertDismissAsync(udid, pid, host, port, button, OutputWriter.ResolveJsonMode(json, noJson)), dismissUdid, dismissPid, agentHostOption, agentPortOption, dismissButtonArg, jsonOption, noJsonOption);
        alertCommand.Add(alertDismissCmd);

        // tree
        var treeUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var treePid = new Option<int?>("--pid", "Mac Catalyst app PID (auto-detects if omitted)");
        var alertTreeCmd = new Command("tree", "Show raw accessibility tree") { treeUdid, treePid };
        alertTreeCmd.SetHandler(async (udid, pid, host, port, json, noJson) =>
            await AlertTreeAsync(udid, pid, host, port, OutputWriter.ResolveJsonMode(json, noJson)), treeUdid, treePid, agentHostOption, agentPortOption, jsonOption, noJsonOption);
        alertCommand.Add(alertTreeCmd);

        mauiCommand.Add(alertCommand);

        // MAUI assert
        var assertIdOption = new Option<string?>("--id", "Element ID to assert on");
        var assertAutoIdOption = new Option<string?>("--automationId", "Resolve element by AutomationId");
        var assertPropertyArg = new Argument<string>("propertyName", "Property to check");
        var assertEqualsArg = new Argument<string>("expectedValue", "Expected value");
        var mauiAssertCmd = new Command("assert", "Assert element property value") { assertIdOption, assertAutoIdOption, assertPropertyArg, assertEqualsArg };
        mauiAssertCmd.SetHandler(async (ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(agentHostOption)!;
            var port = ctx.ParseResult.GetValueForOption(agentPortOption);
            var isJson = OutputWriter.ResolveJsonMode(ctx.ParseResult.GetValueForOption(jsonOption), ctx.ParseResult.GetValueForOption(noJsonOption));
            var id = ctx.ParseResult.GetValueForOption(assertIdOption);
            var autoId = ctx.ParseResult.GetValueForOption(assertAutoIdOption);
            var prop = ctx.ParseResult.GetValueForArgument(assertPropertyArg);
            var expected = ctx.ParseResult.GetValueForArgument(assertEqualsArg);
            await MauiAssertAsync(host, port, isJson, id, autoId, prop, expected);
        });
        mauiCommand.Add(mauiAssertCmd);

        // MAUI permission subcommands (iOS simulator only — uses xcrun simctl privacy)
        var permissionCommand = new Command("permission", "Manage iOS simulator permissions");

        var permGrantUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var permGrantBundle = new Option<string?>("--bundle-id", "App bundle identifier");
        var permGrantServiceArg = new Argument<string>("service", "Permission service (camera, location, photos, contacts, microphone, calendar, all, etc.)");
        var permGrantCmd = new Command("grant", "Grant a permission (no dialog will appear)") { permGrantServiceArg, permGrantUdid, permGrantBundle };
        permGrantCmd.SetHandler(async (udid, bundleId, service) => await PermissionAsync("grant", udid, bundleId, service), permGrantUdid, permGrantBundle, permGrantServiceArg);
        permissionCommand.Add(permGrantCmd);

        var permRevokeUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var permRevokeBundle = new Option<string?>("--bundle-id", "App bundle identifier");
        var permRevokeServiceArg = new Argument<string>("service", "Permission service");
        var permRevokeCmd = new Command("revoke", "Revoke a permission") { permRevokeServiceArg, permRevokeUdid, permRevokeBundle };
        permRevokeCmd.SetHandler(async (udid, bundleId, service) => await PermissionAsync("revoke", udid, bundleId, service), permRevokeUdid, permRevokeBundle, permRevokeServiceArg);
        permissionCommand.Add(permRevokeCmd);

        var permResetUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var permResetBundle = new Option<string?>("--bundle-id", "App bundle identifier");
        var permResetServiceArg = new Argument<string>("service", () => "all", "Permission service (default: all)");
        var permResetCmd = new Command("reset", "Reset permission (app will be prompted again)") { permResetServiceArg, permResetUdid, permResetBundle };
        permResetCmd.SetHandler(async (udid, bundleId, service) => await PermissionAsync("reset", udid, bundleId, service), permResetUdid, permResetBundle, permResetServiceArg);
        permissionCommand.Add(permResetCmd);

        mauiCommand.Add(permissionCommand);

        // logs command
        var logsLimitOption = new Option<int>("--limit", () => 100, "Number of log entries to return");
        var logsSkipOption = new Option<int>("--skip", () => 0, "Number of newest entries to skip");
        var logsSourceOption = new Option<string?>("--source", () => null, "Filter by log source: native, webview, or all (default: all)");
        var logsFollowOption = new Option<bool>("--follow", () => false, "Stream logs in real-time (Ctrl+C to stop)");
        logsFollowOption.AddAlias("-f");
        var logsReplayOption = new Option<int>("--replay", () => 100, "Number of recent entries to replay on connect (use with --follow, 0 to skip)");
        var mauiLogsCmd = new Command("logs", "Fetch application logs") { logsLimitOption, logsSkipOption, logsSourceOption, logsFollowOption, logsReplayOption };
        mauiLogsCmd.SetHandler(async (ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(agentHostOption)!;
            var port = ctx.ParseResult.GetValueForOption(agentPortOption);
            var isJson = OutputWriter.ResolveJsonMode(ctx.ParseResult.GetValueForOption(jsonOption), ctx.ParseResult.GetValueForOption(noJsonOption));
            var follow = ctx.ParseResult.GetValueForOption(logsFollowOption);
            if (follow)
                await MauiLogsFollowAsync(host, port, ctx.ParseResult.GetValueForOption(logsSourceOption), isJson, ctx.ParseResult.GetValueForOption(logsReplayOption));
            else
                await MauiLogsAsync(host, port, isJson, ctx.ParseResult.GetValueForOption(logsLimitOption), ctx.ParseResult.GetValueForOption(logsSkipOption), ctx.ParseResult.GetValueForOption(logsSourceOption));
        });
        mauiCommand.Add(mauiLogsCmd);

        // ── Network monitoring command ──
        var networkCommand = new Command("network", "Monitor HTTP network requests");
        var networkLimitOption = new Option<int>("--limit", () => 100, "Maximum number of entries to show");
        var networkHostOption = new Option<string?>("--host", () => null, "Filter by host");
        var networkMethodOption = new Option<string?>("--method", () => null, "Filter by HTTP method");
        networkCommand.AddOption(networkLimitOption);
        networkCommand.AddOption(networkHostOption);
        networkCommand.AddOption(networkMethodOption);
        networkCommand.SetHandler(async (host, port, json, noJson, limit, filterHost, filterMethod) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            if (isJson)
                await MauiNetworkMonitorAsync(host, port, isJson, limit, filterHost, filterMethod);
            else
                await Microsoft.Maui.DevFlow.CLI.NetworkMonitorTui.RunAsync(host, port, filterHost, filterMethod);
        },
            agentHostOption, agentPortOption, jsonOption, noJsonOption, networkLimitOption, networkHostOption, networkMethodOption);

        var networkListCmd = new Command("list", "List recent network requests (one-shot)");
        networkListCmd.AddOption(networkLimitOption);
        networkListCmd.AddOption(networkHostOption);
        networkListCmd.AddOption(networkMethodOption);
        networkListCmd.SetHandler(async (host, port, json, noJson, limit, filterHost, filterMethod) =>
            await MauiNetworkListAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson), limit, filterHost, filterMethod),
            agentHostOption, agentPortOption, jsonOption, noJsonOption, networkLimitOption, networkHostOption, networkMethodOption);
        networkCommand.Add(networkListCmd);

        var networkDetailId = new Argument<string>("id", "Request ID to show details for");
        var networkDetailCmd = new Command("detail", "Show full request/response details") { networkDetailId };
        networkDetailCmd.SetHandler(async (host, port, json, noJson, id) =>
            await MauiNetworkDetailAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson), id),
            agentHostOption, agentPortOption, jsonOption, noJsonOption, networkDetailId);
        networkCommand.Add(networkDetailCmd);

        var networkClearCmd = new Command("clear", "Clear the network request buffer");
        networkClearCmd.SetHandler(async (host, port, json, noJson) => await MauiNetworkClearAsync(host, port, OutputWriter.ResolveJsonMode(json, noJson)),
            agentHostOption, agentPortOption, jsonOption, noJsonOption);
        networkCommand.Add(networkClearCmd);

        mauiCommand.Add(networkCommand);

        // ===== MAUI preferences subcommands =====
        var prefsCommand = new Command("preferences", "Manage app preferences (key-value store)");

        var prefsSharedNameOption = new Option<string?>("--sharedName", "Shared preferences container name");

        var prefsListCmd = new Command("list", "List all known preference keys") { prefsSharedNameOption };
        prefsListCmd.SetHandler(async (host, port, json, noJson, sharedName) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            var qs = sharedName != null ? $"?sharedName={Uri.EscapeDataString(sharedName)}" : "";
            await SimpleGetAsync(host, port, $"/api/preferences{qs}", isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, prefsSharedNameOption);
        prefsCommand.Add(prefsListCmd);

        var prefsGetKeyArg = new Argument<string>("key", "Preference key");
        var prefsGetTypeOption = new Option<string>("--type", () => "string", "Value type (string|int|bool|double|float|long|datetime)");
        var prefsGetCmd = new Command("get", "Get a preference value") { prefsGetKeyArg, prefsGetTypeOption, prefsSharedNameOption };
        prefsGetCmd.SetHandler(async (host, port, json, noJson, key, type, sharedName) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            var qs = $"?type={Uri.EscapeDataString(type)}";
            if (sharedName != null) qs += $"&sharedName={Uri.EscapeDataString(sharedName)}";
            await SimpleGetAsync(host, port, $"/api/preferences/{Uri.EscapeDataString(key)}{qs}", isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, prefsGetKeyArg, prefsGetTypeOption, prefsSharedNameOption);
        prefsCommand.Add(prefsGetCmd);

        var prefsSetKeyArg = new Argument<string>("key", "Preference key");
        var prefsSetValueArg = new Argument<string>("value", "Value to set");
        var prefsSetTypeOption = new Option<string>("--type", () => "string", "Value type (string|int|bool|double|float|long|datetime)");
        var prefsSetSharedNameOption = new Option<string?>("--sharedName", "Shared preferences container name");
        var prefsSetCmd = new Command("set", "Set a preference value") { prefsSetKeyArg, prefsSetValueArg, prefsSetTypeOption, prefsSetSharedNameOption };
        prefsSetCmd.SetHandler(async (host, port, json, noJson, key, value, type, sharedName) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            var body = new JsonObject
            {
                ["value"] = value,
                ["type"] = type,
                ["sharedName"] = sharedName
            };
            await SimplePostAsync(host, port, $"/api/preferences/{Uri.EscapeDataString(key)}", body, isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, prefsSetKeyArg, prefsSetValueArg, prefsSetTypeOption, prefsSetSharedNameOption);
        prefsCommand.Add(prefsSetCmd);

        var prefsDeleteKeyArg = new Argument<string>("key", "Preference key to remove");
        var prefsDeleteSharedNameOption = new Option<string?>("--sharedName", "Shared preferences container name");
        var prefsDeleteCmd = new Command("delete", "Remove a preference") { prefsDeleteKeyArg, prefsDeleteSharedNameOption };
        prefsDeleteCmd.SetHandler(async (host, port, json, noJson, key, sharedName) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            var qs = sharedName != null ? $"?sharedName={Uri.EscapeDataString(sharedName)}" : "";
            await SimpleDeleteAsync(host, port, $"/api/preferences/{Uri.EscapeDataString(key)}{qs}", isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, prefsDeleteKeyArg, prefsDeleteSharedNameOption);
        prefsCommand.Add(prefsDeleteCmd);

        var prefsClearSharedNameOption = new Option<string?>("--sharedName", "Shared preferences container name");
        var prefsClearCmd = new Command("clear", "Clear all preferences") { prefsClearSharedNameOption };
        prefsClearCmd.SetHandler(async (host, port, json, noJson, sharedName) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            var qs = sharedName != null ? $"?sharedName={Uri.EscapeDataString(sharedName)}" : "";
            await SimplePostAsync(host, port, $"/api/preferences/clear{qs}", null, isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, prefsClearSharedNameOption);
        prefsCommand.Add(prefsClearCmd);

        mauiCommand.Add(prefsCommand);

        // ===== MAUI secure-storage subcommands =====
        var secureCommand = new Command("secure-storage", "Manage secure storage (encrypted key-value store)");

        var secureGetKeyArg = new Argument<string>("key", "Secure storage key");
        var secureGetCmd = new Command("get", "Get a secure storage value") { secureGetKeyArg };
        secureGetCmd.SetHandler(async (host, port, json, noJson, key) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            await SimpleGetAsync(host, port, $"/api/secure-storage/{Uri.EscapeDataString(key)}", isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, secureGetKeyArg);
        secureCommand.Add(secureGetCmd);

        var secureSetKeyArg = new Argument<string>("key", "Secure storage key");
        var secureSetValueArg = new Argument<string>("value", "Value to store");
        var secureSetCmd = new Command("set", "Set a secure storage value") { secureSetKeyArg, secureSetValueArg };
        secureSetCmd.SetHandler(async (host, port, json, noJson, key, value) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            await SimplePostAsync(host, port, $"/api/secure-storage/{Uri.EscapeDataString(key)}", new JsonObject
            {
                ["value"] = value
            }, isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, secureSetKeyArg, secureSetValueArg);
        secureCommand.Add(secureSetCmd);

        var secureDeleteKeyArg = new Argument<string>("key", "Secure storage key to remove");
        var secureDeleteCmd = new Command("delete", "Remove a secure storage entry") { secureDeleteKeyArg };
        secureDeleteCmd.SetHandler(async (host, port, json, noJson, key) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            await SimpleDeleteAsync(host, port, $"/api/secure-storage/{Uri.EscapeDataString(key)}", isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, secureDeleteKeyArg);
        secureCommand.Add(secureDeleteCmd);

        var secureClearCmd = new Command("clear", "Clear all secure storage entries");
        secureClearCmd.SetHandler(async (host, port, json, noJson) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            await SimplePostAsync(host, port, "/api/secure-storage/clear", null, isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption);
        secureCommand.Add(secureClearCmd);

        mauiCommand.Add(secureCommand);

        // ===== MAUI platform subcommands (read-only) =====
        var platformCommand = new Command("platform", "Query platform features and device info");

        var platformAppInfoCmd = new Command("app-info", "Get app name, version, package name, theme");
        platformAppInfoCmd.SetHandler(async (host, port, json, noJson) =>
            await SimpleGetAsync(host, port, "/api/platform/app-info", OutputWriter.ResolveJsonMode(json, noJson)),
            agentHostOption, agentPortOption, jsonOption, noJsonOption);
        platformCommand.Add(platformAppInfoCmd);

        var platformDeviceInfoCmd = new Command("device-info", "Get device manufacturer, model, OS version");
        platformDeviceInfoCmd.SetHandler(async (host, port, json, noJson) =>
            await SimpleGetAsync(host, port, "/api/platform/device-info", OutputWriter.ResolveJsonMode(json, noJson)),
            agentHostOption, agentPortOption, jsonOption, noJsonOption);
        platformCommand.Add(platformDeviceInfoCmd);

        var platformDisplayCmd = new Command("display", "Get screen density, size, orientation");
        platformDisplayCmd.SetHandler(async (host, port, json, noJson) =>
            await SimpleGetAsync(host, port, "/api/platform/device-display", OutputWriter.ResolveJsonMode(json, noJson)),
            agentHostOption, agentPortOption, jsonOption, noJsonOption);
        platformCommand.Add(platformDisplayCmd);

        var platformBatteryCmd = new Command("battery", "Get battery level, state, power source");
        platformBatteryCmd.SetHandler(async (host, port, json, noJson) =>
            await SimpleGetAsync(host, port, "/api/platform/battery", OutputWriter.ResolveJsonMode(json, noJson)),
            agentHostOption, agentPortOption, jsonOption, noJsonOption);
        platformCommand.Add(platformBatteryCmd);

        var platformConnectivityCmd = new Command("connectivity", "Get network access and connection profiles");
        platformConnectivityCmd.SetHandler(async (host, port, json, noJson) =>
            await SimpleGetAsync(host, port, "/api/platform/connectivity", OutputWriter.ResolveJsonMode(json, noJson)),
            agentHostOption, agentPortOption, jsonOption, noJsonOption);
        platformCommand.Add(platformConnectivityCmd);

        var platformVersionTrackingCmd = new Command("version-tracking", "Get version history and first launch info");
        platformVersionTrackingCmd.SetHandler(async (host, port, json, noJson) =>
            await SimpleGetAsync(host, port, "/api/platform/version-tracking", OutputWriter.ResolveJsonMode(json, noJson)),
            agentHostOption, agentPortOption, jsonOption, noJsonOption);
        platformCommand.Add(platformVersionTrackingCmd);

        var platformPermsNameArg = new Argument<string?>("permission", () => null, "Permission name (e.g., camera, locationWhenInUse). Omit to check all.");
        var platformPermsCmd = new Command("permissions", "Check permission status") { platformPermsNameArg };
        platformPermsCmd.SetHandler(async (host, port, json, noJson, permName) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            var path = permName != null
                ? $"/api/platform/permissions/{Uri.EscapeDataString(permName)}"
                : "/api/platform/permissions";
            await SimpleGetAsync(host, port, path, isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, platformPermsNameArg);
        platformCommand.Add(platformPermsCmd);

        var platformGeoAccuracyOption = new Option<string>("--accuracy", () => "Medium", "Accuracy (Lowest|Low|Medium|High|Best)");
        var platformGeoTimeoutOption = new Option<int>("--timeout", () => 10, "Timeout in seconds");
        var platformGeoCmd = new Command("geolocation", "Get current GPS coordinates") { platformGeoAccuracyOption, platformGeoTimeoutOption };
        platformGeoCmd.SetHandler(async (host, port, json, noJson, accuracy, timeout) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            await SimpleGetAsync(host, port, $"/api/platform/geolocation?accuracy={Uri.EscapeDataString(accuracy)}&timeout={timeout}", isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, platformGeoAccuracyOption, platformGeoTimeoutOption);
        platformCommand.Add(platformGeoCmd);

        mauiCommand.Add(platformCommand);

        // ===== MAUI sensors subcommands =====
        var sensorsCommand = new Command("sensors", "Monitor device sensors");

        var sensorsListCmd = new Command("list", "List available sensors and their status");
        sensorsListCmd.SetHandler(async (host, port, json, noJson) =>
            await SimpleGetAsync(host, port, "/api/sensors", OutputWriter.ResolveJsonMode(json, noJson)),
            agentHostOption, agentPortOption, jsonOption, noJsonOption);
        sensorsCommand.Add(sensorsListCmd);

        var sensorsStartSensorArg = new Argument<string>("sensor", "Sensor name (accelerometer, barometer, compass, gyroscope, magnetometer, orientation)");
        var sensorsStartSpeedOption = new Option<string>("--speed", () => "UI", "Sensor speed (UI|Game|Fastest|Default)");
        var sensorsStartCmd = new Command("start", "Start a sensor") { sensorsStartSensorArg, sensorsStartSpeedOption };
        sensorsStartCmd.SetHandler(async (host, port, json, noJson, sensor, speed) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            await SimplePostAsync(host, port, $"/api/sensors/{Uri.EscapeDataString(sensor)}/start?speed={Uri.EscapeDataString(speed)}", null, isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, sensorsStartSensorArg, sensorsStartSpeedOption);
        sensorsCommand.Add(sensorsStartCmd);

        var sensorsStopSensorArg = new Argument<string>("sensor", "Sensor name");
        var sensorsStopCmd = new Command("stop", "Stop a sensor") { sensorsStopSensorArg };
        sensorsStopCmd.SetHandler(async (host, port, json, noJson, sensor) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            await SimplePostAsync(host, port, $"/api/sensors/{Uri.EscapeDataString(sensor)}/stop", null, isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, sensorsStopSensorArg);
        sensorsCommand.Add(sensorsStopCmd);

        var sensorsStreamSensorArg = new Argument<string>("sensor", "Sensor name to stream");
        var sensorsStreamSpeedOption = new Option<string>("--speed", () => "UI", "Sensor speed (UI|Game|Fastest|Default)");
        var sensorsStreamDurationOption = new Option<int>("--duration", () => 0, "Duration in seconds (0 = indefinite, Ctrl+C to stop)");
        var sensorsStreamThrottleOption = new Option<int>("--throttle", () => 100, "Minimum ms between readings (default 100 = ~10/sec, 0 = no throttle)");
        var sensorsStreamCmd = new Command("stream", "Stream sensor readings via WebSocket") { sensorsStreamSensorArg, sensorsStreamSpeedOption, sensorsStreamDurationOption, sensorsStreamThrottleOption };
        sensorsStreamCmd.SetHandler(async (host, port, json, noJson, sensor, speed, duration, throttle) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            await SensorStreamAsync(host, port, sensor, speed, duration, throttle, isJson);
        }, agentHostOption, agentPortOption, jsonOption, noJsonOption, sensorsStreamSensorArg, sensorsStreamSpeedOption, sensorsStreamDurationOption, sensorsStreamThrottleOption);
        sensorsCommand.Add(sensorsStreamCmd);

        mauiCommand.Add(sensorsCommand);

        rootCommand.Add(mauiCommand);

        // ===== update-skill command =====
        var forceOption = new Option<bool>(
            ["--force", "-y"],
            "Skip confirmation prompt");
        var outputDirOption = new Option<string?>(
            ["--output", "-o"],
            "Output directory (defaults to current directory)");
        var branchOption = new Option<string>(
            ["--branch", "-b"],
            () => "main",
            "GitHub branch to download from");
        var updateSkillCmd = new Command("update-skill", "Download the latest maui-ai-debugging skill from GitHub")
        {
            forceOption, outputDirOption, branchOption
        };
        updateSkillCmd.SetHandler(async (force, output, branch) => await UpdateSkillAsync(force, output, branch), forceOption, outputDirOption, branchOption);
        rootCommand.Add(updateSkillCmd);

        // ===== skill-version command =====
        var skillVersionOutputOption = new Option<string?>(
            ["--output", "-o"],
            "Skill directory (defaults to current directory)");
        var skillVersionBranchOption = new Option<string>(
            ["--branch", "-b"],
            () => "main",
            "GitHub branch to check against");
        var skillVersionCmd = new Command("skill-version", "Check the installed skill version and compare with remote")
        {
            skillVersionOutputOption, skillVersionBranchOption
        };
        skillVersionCmd.SetHandler(async (output, branch) => await SkillVersionAsync(output, branch), skillVersionOutputOption, skillVersionBranchOption);
        rootCommand.Add(skillVersionCmd);

        // ===== broker commands =====
        var brokerCommand = new Command("broker", "Manage the Microsoft.Maui.DevFlow broker daemon");

        var brokerForegroundOption = new Option<bool>("--foreground", "Run in foreground (don't detach)");
        var brokerStartCmd = new Command("start", "Start the broker daemon") { brokerForegroundOption };
        brokerStartCmd.SetHandler(async (foreground) => await BrokerStartAsync(foreground), brokerForegroundOption);
        brokerCommand.Add(brokerStartCmd);

        var brokerStopCmd = new Command("stop", "Stop the broker daemon");
        brokerStopCmd.SetHandler(async () => await BrokerStopAsync());
        brokerCommand.Add(brokerStopCmd);

        var brokerStatusCmd = new Command("status", "Show broker daemon status");
        brokerStatusCmd.SetHandler(async (json, noJson) => await BrokerStatusAsync(OutputWriter.ResolveJsonMode(json, noJson)), jsonOption, noJsonOption);
        brokerCommand.Add(brokerStatusCmd);

        var brokerLogCmd = new Command("log", "Show broker log");
        brokerLogCmd.SetHandler(() => BrokerLogAsync());
        brokerCommand.Add(brokerLogCmd);

        rootCommand.Add(brokerCommand);

        // ===== list command (agent discovery) =====
        var listCmd = new Command("list", "List all connected agents");
        listCmd.SetHandler(async (json, noJson) => await ListAgentsCommandAsync(OutputWriter.ResolveJsonMode(json, noJson)), jsonOption, noJsonOption);
        rootCommand.Add(listCmd);

        // ===== diagnose command (end-to-end diagnostics) =====
        var diagnoseCmd = new Command("diagnose", "Check DevFlow health: broker, agents, and project integration");
        diagnoseCmd.SetHandler(async (json, noJson) => await DiagnoseCommandAsync(OutputWriter.ResolveJsonMode(json, noJson)), jsonOption, noJsonOption);
        rootCommand.Add(diagnoseCmd);

        // ===== wait command (wait for agent to connect) =====
        var waitTimeoutOption = new Option<int>(
            ["--timeout", "-t"],
            () => 120,
            "Maximum seconds to wait for an agent to connect");
        var waitProjectOption = new Option<string?>(
            ["--project"],
            () => null,
            "Filter by project path (csproj). Resolves to full path for matching.");
        var waitPlatformOption = new Option<string?>(
            ["--wait-platform"],
            () => null,
            "Filter by platform (e.g., macOS, iOS, Android)");
        var waitCmd = new Command("wait", "Wait for an agent to connect to the broker")
        {
            waitTimeoutOption, waitProjectOption, waitPlatformOption
        };
        waitCmd.SetHandler(async (timeout, project, waitPlatform, json, noJson) =>
            await WaitForAgentCommandAsync(timeout, project, waitPlatform, OutputWriter.ResolveJsonMode(json, noJson)),
            waitTimeoutOption, waitProjectOption, waitPlatformOption, jsonOption, noJsonOption);
        rootCommand.Add(waitCmd);

        // ===== batch command (interactive stdin/stdout) =====
        var batchDelayOption = new Option<int>("--delay", () => 250, "Delay in ms between commands");
        var batchContinueOption = new Option<bool>("--continue-on-error", () => false, "Continue executing after a command fails");
        var batchHumanOption = new Option<bool>("--human", () => false, "Human-readable output instead of JSONL");
        var batchCommand = new Command("batch", "Execute commands from stdin with JSONL responses on stdout")
        {
            batchDelayOption, batchContinueOption, batchHumanOption
        };
        batchCommand.SetHandler(async (host, port, delay, continueOnError, human) =>
            await BatchAsync(host, port, delay, continueOnError, human),
            agentHostOption, agentPortOption, batchDelayOption, batchContinueOption, batchHumanOption);
        rootCommand.Add(batchCommand);

        // ===== version command =====
        var versionCmd = new Command("version", "Show CLI version information");
        versionCmd.SetHandler(() =>
        {
            var version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            Console.WriteLine($"maui-devflow {version}");
        });
        rootCommand.Add(versionCmd);

        // ===== commands command (schema discovery) =====
        var commandsCmd = new Command("commands", "List all available commands (machine-readable schema discovery)");
        commandsCmd.SetHandler((json, noJson) =>
        {
            var isJson = OutputWriter.ResolveJsonMode(json, noJson);
            var cmds = GetCommandDescriptions();
            OutputWriter.WriteResult(cmds, isJson, list =>
            {
                Console.WriteLine($"{"Command",-35} {"Mutating",-10} {"Description"}");
                Console.WriteLine(new string('-', 85));
                foreach (var c in list)
                    Console.WriteLine($"{c.Command,-35} {(c.Mutating ? "yes" : "no"),-10} {c.Description}");
            });
        }, jsonOption, noJsonOption);
        rootCommand.Add(commandsCmd);

        // ===== MCP server command =====
        var mcpServeCmd = new Command("mcp-serve", "Start MCP (Model Context Protocol) server for AI agent integration via stdio");
        mcpServeCmd.SetHandler(async () => await Mcp.McpServerHost.RunAsync());
        rootCommand.Add(mcpServeCmd);

        _parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build();

        _errorOccurred = false;
        var result = await _parser.InvokeAsync(args);
        return _errorOccurred ? 1 : result;
    }
    
    // ===== CDP Helper: Send command via AgentClient =====

    private static async Task<JsonElement?> SendCdpCommandAsync(string host, int port, string method, JsonNode? parameters = null, string? webview = null)
    {
        using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
        var result = await client.SendCdpCommandAsync(method, parameters, webview);
        return result;
    }

    private static async Task<string> CdpEvaluateAsync(string host, int port, string expression, string? webview = null)
    {
        var result = await SendCdpCommandAsync(host, port, "Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true
        }, webview);

        if (result == null) return "null";
        var root = result.Value;

        if (root.TryGetProperty("result", out var evalResult))
        {
            if (evalResult.TryGetProperty("result", out var resultProp))
            {
                if (resultProp.TryGetProperty("value", out var value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                        return value.GetString() ?? "null";
                    if (value.ValueKind == JsonValueKind.Object || value.ValueKind == JsonValueKind.Array)
                        return CliJson.PrettyPrint(value);
                    return value.ToString();
                }
            }
            if (evalResult.TryGetProperty("exceptionDetails", out var exception))
            {
                var text = exception.TryGetProperty("text", out var t) ? t.GetString() : "Unknown error";
                return $"Error: {text}";
            }
        }

        // Response might be the raw chobitsu response
        if (root.TryGetProperty("result", out var rawResult) && rawResult.TryGetProperty("value", out var rawValue))
            return rawValue.GetString() ?? rawValue.ToString();

        return root.ToString();
    }

    // ===== Browser Domain =====
    
    private static async Task BrowserGetVersionAsync(string host, int port, string? webview = null)
    {
        try
        {
            var result = await SendCdpCommandAsync(host, port, "Browser.getVersion", webview: webview);
            Console.WriteLine(result.HasValue ? FormatJson(result.Value) : "null");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    // ===== Runtime Domain =====
    
    private static async Task RuntimeEvaluateAsync(string host, int port, string expression, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, expression, webview);
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    // ===== DOM Domain =====
    
    private static async Task DomGetDocumentAsync(string host, int port, string? webview = null)
    {
        try
        {
            var result = await SendCdpCommandAsync(host, port, "DOM.getDocument", webview: webview);
            Console.WriteLine(result.HasValue ? FormatJson(result.Value) : "null");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task DomQuerySelectorAsync(string host, int port, string selector, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, $@"
                JSON.stringify((function() {{
                    const el = document.querySelector({CliJson.SerializeUntyped(selector, indented: false)}, webview);
                    if (!el) return null;
                    return {{
                        tagName: el.tagName.toLowerCase(),
                        id: el.id || null,
                        className: el.className || null,
                        textContent: el.textContent?.trim().substring(0, 100) || null
                    }};
                }})())
            ");
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task DomQuerySelectorAllAsync(string host, int port, string selector, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, $@"
                JSON.stringify((function() {{
                    const els = document.querySelectorAll({CliJson.SerializeUntyped(selector, indented: false)}, webview);
                    return Array.from(els).map((el, i) => ({{
                        index: i,
                        tagName: el.tagName.toLowerCase(),
                        id: el.id || null,
                        className: el.className || null,
                        textContent: el.textContent?.trim().substring(0, 50) || null
                    }}));
                }})(), null, 2)
            ");
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task DomGetOuterHtmlAsync(string host, int port, string selector, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, $@"document.querySelector({CliJson.SerializeUntyped(selector, indented: false)})?.outerHTML || null", webview);
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    // ===== Page Domain =====
    
    private static async Task PageNavigateAsync(string host, int port, string url, string? webview = null)
    {
        try
        {
            await SendCdpCommandAsync(host, port, "Page.navigate", new JsonObject
            {
                ["url"] = url
            }, webview);
            Console.WriteLine($"Navigated to: {url}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task PageReloadAsync(string host, int port, string? webview = null)
    {
        try
        {
            await SendCdpCommandAsync(host, port, "Page.reload", webview: webview);
            Console.WriteLine("Page reloaded");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task PageCaptureScreenshotAsync(string host, int port, string? webview = null)
    {
        try
        {
            var result = await SendCdpCommandAsync(host, port, "Page.captureScreenshot", webview: webview);
            if (result.HasValue &&
                result.Value.TryGetProperty("result", out var resultProp) && 
                resultProp.TryGetProperty("data", out var dataProp))
            {
                Console.WriteLine(dataProp.GetString());
            }
            else
            {
                Console.WriteLine(result.HasValue ? FormatJson(result.Value) : "null");
            }
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    // ===== Input Domain =====
    
    private static async Task InputDispatchClickAsync(string host, int port, string selector, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, $@"
                (function() {{
                    const el = document.querySelector({CliJson.SerializeUntyped(selector, indented: false)}, webview);
                    if (!el) return 'Error: Element not found';
                    el.click();
                    return 'Clicked: ' + el.tagName.toLowerCase() + (el.id ? '#' + el.id : '');
                }})()
            ");
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task InputInsertTextAsync(string host, int port, string text, string? webview = null)
    {
        try
        {
            await SendCdpCommandAsync(host, port, "Input.insertText", new JsonObject
            {
                ["text"] = text
            }, webview);
            Console.WriteLine($"Inserted: {text.Length} characters");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task InputFillAsync(string host, int port, string selector, string text, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, $@"
                (function() {{
                    const el = document.querySelector({CliJson.SerializeUntyped(selector, indented: false)}, webview);
                    if (!el) return 'Error: Element not found';
                    
                    const text = {CliJson.SerializeUntyped(text, indented: false)};
                    if (el.isContentEditable) {{
                        el.textContent = text;
                    }} else {{
                        el.value = text;
                        el.focus();
                    }}
                    el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                    return 'Filled: ' + el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + ' with ' + text.length + ' chars';
                }})()
            ");
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    // ===== Convenience Commands =====
    
    private static async Task CdpStatusAsync(string host, int port, string? webview = null)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var response = await http.GetAsync($"http://{host}:{port}/api/status");
            var body = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var cdpReady = root.TryGetProperty("cdpReady", out var cdpProp) && cdpProp.GetBoolean();
            var cdpCount = root.TryGetProperty("cdpWebViewCount", out var countProp) ? countProp.GetInt32() : 0;
            Console.WriteLine(cdpReady
                ? $"Connected: CDP ready ({cdpCount} WebView{(cdpCount != 1 ? "s" : "")})"
                : "Agent connected but CDP not ready");
        }
        catch (Exception ex)
        {
            WriteError($"Not connected: {ex.Message}");
        }
    }

    private static async Task CdpWebViewsAsync(string host, int port, bool json)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var result = await client.GetCdpWebViewsAsync();
            var body = result.ToString();

            if (json)
            {
                Console.WriteLine(body);
                return;
            }

            if (result.TryGetProperty("webviews", out var webviews))
            {
                if (webviews.GetArrayLength() == 0)
                {
                    Console.WriteLine("No CDP WebViews registered");
                    return;
                }

                Console.WriteLine($"{"Index",-6} {"AutomationId",-20} {"ElementId",-12} {"Ready",-6} {"URL"}");
                Console.WriteLine(new string('-', 70));
                foreach (var wv in webviews.EnumerateArray())
                {
                    var index = wv.TryGetProperty("index", out var idx) ? idx.GetInt32().ToString() : "-";
                    var autoId = wv.TryGetProperty("automationId", out var aid) ? aid.GetString() ?? "-" : "-";
                    var elemId = wv.TryGetProperty("elementId", out var eid) ? eid.GetString() ?? "-" : "-";
                    var ready = wv.TryGetProperty("isReady", out var rdy) && rdy.GetBoolean() ? "Yes" : "No";
                    var url = wv.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "-" : "-";
                    Console.WriteLine($"{index,-6} {autoId,-20} {elemId,-12} {ready,-6} {url}");
                }
            }
        }
        catch (Exception ex)
        {
            WriteError($"Failed to list WebViews: {ex.Message}");
        }
    }
    
    private static async Task CdpSourceAsync(string host, int port, string? webview = null)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var source = await client.GetCdpSourceAsync(webview);
            Console.WriteLine(source);
        }
        catch (Exception ex)
        {
            WriteError($"Failed to get page source: {ex.Message}");
        }
    }

    private static async Task SnapshotAsync(string host, int port, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, @"
                (function() {
                    function walk(node, depth) {
                        if (depth > 8) return '';
                        let result = '';
                        const indent = '  '.repeat(depth, webview);
                        
                        if (node.nodeType === 1) {
                            const tag = node.tagName.toLowerCase();
                            const text = node.childNodes.length === 1 && node.childNodes[0].nodeType === 3 
                                ? node.textContent?.trim().substring(0, 80) : null;
                            const isClickable = node.onclick || tag === 'button' || tag === 'a' || 
                                               node.getAttribute('role') === 'button' ||
                                               (tag === 'input' && node.type === 'submit');
                            const isInput = tag === 'input' || tag === 'textarea' || tag === 'select';
                            
                            result += indent + '<' + tag;
                            if (node.id) result += ' id=""' + node.id + '""';
                            if (node.className && typeof node.className === 'string') result += ' class=""' + node.className.split(' ').slice(0,2).join(' ') + '""';
                            if (isClickable) result += ' [clickable]';
                            if (isInput) result += ' [input]';
                            if (tag === 'a' && node.href) result += ' href=""' + node.getAttribute('href') + '""';
                            if (tag === 'input') result += ' type=""' + (node.type || 'text') + '""';
                            result += '>';
                            if (text) result += ' ' + text;
                            result += '\n';
                            
                            for (const child of node.children) {
                                result += walk(child, depth + 1);
                            }
                        }
                        return result;
                    }
                    
                    return 'Title: ' + document.title + '\nURL: ' + location.href + '\n\n' + walk(document.body, 0);
                })()
            ");
            
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static void WriteError(string message)
    {
        _errorOccurred = true;
        Console.Error.WriteLine($"Error: {message}");
    }
    
    private class CommandErrorException : Exception
    {
        public CommandErrorException(string message) : base(message) { }
    }
    
    private static string FormatJson(JsonElement element)
    {
        return CliJson.PrettyPrint(element);
    }

    // ===== Generic agent HTTP helpers (for preferences, platform, sensors, etc.) =====

    private static async Task SimpleGetAsync(string host, int port, string path, bool json)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            var response = await http.GetAsync($"http://{host}:{port}{path}");
            var body = await response.Content.ReadAsStringAsync();
            if (json || !response.IsSuccessStatusCode)
            {
                Console.WriteLine(body);
            }
            else
            {
                try
                {
                    Console.WriteLine(CliJson.PrettyPrint(body));
                }
                catch
                {
                    Console.WriteLine(body);
                }
            }
            if (!response.IsSuccessStatusCode) _errorOccurred = true;
        }
        catch (Exception ex)
        {
            OutputWriter.WriteError(ex.Message, json);
            _errorOccurred = true;
        }
    }

    private static async Task SimplePostAsync(string host, int port, string path, JsonNode? bodyObj, bool json)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            HttpResponseMessage response;
            if (bodyObj != null)
            {
                using var content = new StringContent(
                    CliJson.SerializeUntyped(bodyObj, indented: false),
                    Encoding.UTF8,
                    "application/json");
                response = await http.PostAsync($"http://{host}:{port}{path}", content);
            }
            else
            {
                response = await http.PostAsync($"http://{host}:{port}{path}", null);
            }
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine(body);
            if (!response.IsSuccessStatusCode) _errorOccurred = true;
        }
        catch (Exception ex)
        {
            OutputWriter.WriteError(ex.Message, json);
            _errorOccurred = true;
        }
    }

    private static async Task SimpleDeleteAsync(string host, int port, string path, bool json)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            var response = await http.DeleteAsync($"http://{host}:{port}{path}");
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine(body);
            if (!response.IsSuccessStatusCode) _errorOccurred = true;
        }
        catch (Exception ex)
        {
            OutputWriter.WriteError(ex.Message, json);
            _errorOccurred = true;
        }
    }

    private static async Task SensorStreamAsync(string host, int port, string sensor, string speed, int duration, int throttleMs, bool json)
    {
        try
        {
            using var client = new System.Net.WebSockets.ClientWebSocket();
            var uri = new Uri($"ws://{host}:{port}/ws/sensors?sensor={Uri.EscapeDataString(sensor)}&speed={Uri.EscapeDataString(speed)}&throttleMs={throttleMs}");
            using var cts = duration > 0
                ? new CancellationTokenSource(TimeSpan.FromSeconds(duration))
                : new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            await client.ConnectAsync(uri, cts.Token);
            var buffer = new byte[4096];

            while (!cts.Token.IsCancellationRequested && client.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var result = await client.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    break;

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Console.WriteLine(message);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal exit via Ctrl+C or duration timeout
        }
        catch (Exception ex)
        {
            OutputWriter.WriteError(ex.Message, json);
            _errorOccurred = true;
        }
    }

    // ===== Element Resolution Helper =====

    /// <summary>
    /// Resolve an element ID from either a direct ID or query options (--automationId, --type, --text).
    /// Returns null and writes error if resolution fails.
    /// </summary>
    private static async Task<string?> ResolveElementIdAsync(string host, int port, bool json,
        string? elementId, string? automationId, string? type, string? text, int index)
    {
        // Direct ID takes priority
        if (!string.IsNullOrWhiteSpace(elementId))
        {
            ValidateElementId(elementId, json);
            return elementId;
        }

        // Need at least one resolution option
        if (string.IsNullOrWhiteSpace(automationId) && string.IsNullOrWhiteSpace(type) && string.IsNullOrWhiteSpace(text))
        {
            OutputWriter.WriteError("Provide an element ID or use --automationId, --type, or --text to resolve", json, "InvocationError");
            _errorOccurred = true;
            return null;
        }

        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var results = await client.QueryAsync(type, automationId, text);

            if (results.Count == 0)
            {
                var criteria = new List<string>();
                if (automationId != null) criteria.Add($"automationId=\"{automationId}\"");
                if (type != null) criteria.Add($"type=\"{type}\"");
                if (text != null) criteria.Add($"text=\"{text}\"");
                OutputWriter.WriteError($"No elements found matching {string.Join(", ", criteria)}", json,
                    suggestions: new[] { "Run 'MAUI tree' to see available elements", "Check automationId spelling" });
                _errorOccurred = true;
                return null;
            }

            if (index >= results.Count)
            {
                OutputWriter.WriteError($"Index {index} out of range (found {results.Count} element(s))", json, "RuntimeError",
                    suggestions: new[] { $"Use --index 0 through {results.Count - 1}" });
                _errorOccurred = true;
                return null;
            }

            return results[index].Id;
        }
        catch (Exception ex)
        {
            OutputWriter.WriteError(ex.Message, json);
            _errorOccurred = true;
            return null;
        }
    }

    /// <summary>
    /// Validate element ID for common agent mistakes (control chars, embedded query params).
    /// </summary>
    private static void ValidateElementId(string id, bool json)
    {
        if (id.Any(c => c < 0x20))
        {
            OutputWriter.WriteError($"Element ID contains control characters: '{id}'", json, "InvocationError",
                suggestions: new[] { "Element IDs should not contain control characters", "Run 'MAUI tree' to get valid IDs" });
            _errorOccurred = true;
            return;
        }
        if (id.Contains('?') || id.Contains('#'))
        {
            OutputWriter.WriteError($"Element ID contains '?' or '#': '{id}' — this looks like a URL fragment, not an element ID", json, "InvocationError",
                suggestions: new[] { "Run 'MAUI tree' to get valid element IDs" });
            _errorOccurred = true;
            return;
        }
        if (id.Contains('%'))
        {
            Console.Error.WriteLine($"Warning: Element ID contains '%': '{id}' — possible double-encoding");
        }
    }

    /// <summary>
    /// Handle post-action flags (--and-screenshot, --and-tree) after a mutating command.
    /// </summary>
    private static async Task HandlePostActionFlags(string host, int port, bool json,
        bool hasAndScreenshot, string? andScreenshotPath, bool andTree, int andTreeDepth)
    {
        if (hasAndScreenshot)
        {
            await MauiScreenshotAsync(host, port, json, andScreenshotPath, null, null, null, false, null);
        }
        if (andTree)
        {
            await MauiTreeAsync(host, port, json, andTreeDepth, null, null, null);
        }
    }

    // ===== Assert Command =====

    private static async Task MauiAssertAsync(string host, int port, bool json, string? elementId, string? automationId, string propertyName, string expectedValue)
    {
        try
        {
            var resolvedId = await ResolveElementIdAsync(host, port, json, elementId, automationId, null, null, 0);
            if (resolvedId == null) return;

            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var actualValue = await client.GetPropertyAsync(resolvedId, propertyName);

            var passed = string.Equals(actualValue, expectedValue, StringComparison.Ordinal);
            if (json)
            {
                OutputWriter.WriteResult(new JsonObject
                {
                    ["passed"] = passed,
                    ["property"] = propertyName,
                    ["expected"] = expectedValue,
                    ["actual"] = actualValue,
                    ["elementId"] = resolvedId
                }, json);
            }
            else
            {
                if (passed)
                    Console.WriteLine($"PASS: {propertyName} == \"{expectedValue}\"");
                else
                {
                    Console.WriteLine($"FAIL: {propertyName} expected \"{expectedValue}\" but got \"{actualValue ?? "(null)"}\"");
                    _errorOccurred = true;
                }
            }
            if (!passed) _errorOccurred = true;
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    // ===== Command Descriptions (Schema Discovery) =====

    private static List<CommandDescription> GetCommandDescriptions() => new()
    {
        new("MAUI status", "Check agent connection and app info", false),
        new("MAUI tree", "Dump visual element tree", false),
        new("MAUI query", "Find elements by type, automationId, text, or CSS selector", false),
        new("MAUI element", "Get detailed element info by ID", false),
        new("MAUI hittest", "Find elements at screen coordinates", false),
        new("MAUI tap", "Tap a UI element", true),
        new("MAUI fill", "Fill text into an input element", true),
        new("MAUI clear", "Clear text from an input element", true),
        new("MAUI focus", "Set focus to an element", true),
        new("MAUI navigate", "Navigate to a Shell route", true),
        new("MAUI scroll", "Scroll content or scroll element into view", true),
        new("MAUI resize", "Resize app window", true),
        new("MAUI property", "Get element property value", false),
        new("MAUI set-property", "Set element property value", true),
        new("MAUI screenshot", "Take screenshot of app or element", false),
        new("MAUI assert", "Assert element property equals expected value", false),
        new("MAUI recording start", "Start screen recording", true),
        new("MAUI recording stop", "Stop screen recording", true),
        new("MAUI recording status", "Check recording status", false),
        new("MAUI alert detect", "Check if a system dialog is visible", false),
        new("MAUI alert dismiss", "Dismiss a system dialog", true),
        new("MAUI alert tree", "Show accessibility tree for dialog detection", false),
        new("MAUI permission grant", "Grant iOS simulator permission", true),
        new("MAUI permission revoke", "Revoke iOS simulator permission", true),
        new("MAUI permission reset", "Reset iOS simulator permission", true),
        new("MAUI logs", "Fetch or stream application logs", false),
        new("MAUI network", "Monitor HTTP network requests (live)", false),
        new("MAUI network list", "List recent network requests", false),
        new("MAUI network detail", "Show full network request details", false),
        new("MAUI network clear", "Clear network request buffer", true),
        new("MAUI preferences list", "List all known preference keys", false),
        new("MAUI preferences get", "Get a preference value by key", false),
        new("MAUI preferences set", "Set a preference value", true),
        new("MAUI preferences delete", "Remove a preference", true),
        new("MAUI preferences clear", "Clear all preferences", true),
        new("MAUI secure-storage get", "Get a secure storage value", false),
        new("MAUI secure-storage set", "Set a secure storage value", true),
        new("MAUI secure-storage delete", "Remove a secure storage entry", true),
        new("MAUI secure-storage clear", "Clear all secure storage", true),
        new("MAUI platform app-info", "Get app name, version, theme", false),
        new("MAUI platform device-info", "Get device manufacturer, model, OS", false),
        new("MAUI platform display", "Get screen density, size, orientation", false),
        new("MAUI platform battery", "Get battery level, state, power source", false),
        new("MAUI platform connectivity", "Get network access and profiles", false),
        new("MAUI platform version-tracking", "Get version history and launch info", false),
        new("MAUI platform permissions", "Check permission status", false),
        new("MAUI platform geolocation", "Get current GPS coordinates", false),
        new("MAUI sensors list", "List available sensors and status", false),
        new("MAUI sensors start", "Start a device sensor", true),
        new("MAUI sensors stop", "Stop a device sensor", true),
        new("MAUI sensors stream", "Stream sensor readings via WebSocket", false),
        new("cdp webviews", "List available CDP WebViews", false),
        new("cdp status", "Check CDP connection status", false),
        new("cdp Browser getVersion", "Get browser version", false),
        new("cdp Runtime evaluate", "Evaluate JavaScript expression", false),
        new("cdp DOM getDocument", "Get DOM document tree", false),
        new("cdp DOM querySelector", "Find element by CSS selector", false),
        new("cdp DOM querySelectorAll", "Find all elements by CSS selector", false),
        new("cdp DOM getOuterHTML", "Get element outer HTML", false),
        new("cdp Input click", "Click element by CSS selector", true),
        new("cdp Input insertText", "Insert text at cursor", true),
        new("cdp Input fill", "Fill form field by CSS selector", true),
        new("cdp Page navigate", "Navigate WebView to URL", true),
        new("cdp Page reload", "Reload WebView page", true),
        new("cdp Page captureScreenshot", "Take WebView screenshot", false),
        new("cdp snapshot", "Get simplified DOM snapshot", false),
        new("cdp source", "Get page HTML source", false),
        new("list", "List all connected agents", false),
        new("wait", "Wait for an agent to connect", false),
        new("batch", "Execute commands from stdin", true),
        new("broker start", "Start the broker daemon", true),
        new("broker stop", "Stop the broker daemon", true),
        new("broker status", "Show broker status", false),
        new("broker log", "Show broker log", false),
        new("commands", "List all available commands", false),
        new("version", "Show CLI version", false),
    };

    // ===== Update Skill Command =====

    private const string SkillRepo = "dotnet/maui-labs";
    private const string SkillBasePath = ".claude/skills/maui-ai-debugging";

    private static async Task UpdateSkillAsync(bool force, string? outputDir, string branch)
    {
        var root = outputDir ?? Directory.GetCurrentDirectory();
        var destBase = Path.Combine(root, SkillBasePath);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Microsoft.Maui.DevFlow-CLI", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // Discover files via GitHub Trees API (recursive)
        Console.WriteLine("Fetching skill file list from GitHub...");
        List<string> files;
        try
        {
            files = await GetSkillFilesFromGitHubAsync(http, branch);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to fetch file list: {ex.Message}");
            return;
        }

        if (files.Count == 0)
        {
            Console.Error.WriteLine("No skill files found in the repository.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("maui-devflow update-skill");
        Console.WriteLine($"  Source: https://github.com/{SkillRepo}/tree/{branch}/{SkillBasePath}");
        Console.WriteLine($"  Destination: {destBase}");
        Console.WriteLine();
        Console.WriteLine("Files to download:");
        foreach (var file in files)
        {
            var destPath = Path.Combine(destBase, file);
            var exists = File.Exists(destPath);
            Console.WriteLine($"  {SkillBasePath}/{file}{(exists ? " (overwrite)" : " (new)")}");
        }
        Console.WriteLine();

        if (!force)
        {
            Console.Write("Existing files will be overwritten. Continue? [y/N] ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response is not ("y" or "yes"))
            {
                Console.WriteLine("Cancelled.");
                return;
            }
        }

        var success = 0;
        foreach (var file in files)
        {
            var url = $"https://raw.githubusercontent.com/{SkillRepo}/{branch}/{SkillBasePath}/{file}";
            var destPath = Path.Combine(destBase, file);

            try
            {
                var content = await http.GetStringAsync(url);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                await File.WriteAllTextAsync(destPath, content);
                Console.WriteLine($"  ✓ {file}");
                success++;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"  ✗ {file}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(success == files.Count
            ? $"Done. {success} files updated."
            : $"Done. {success}/{files.Count} files updated.");

        // Write .skill-version with the latest commit SHA
        await WriteSkillVersionAsync(http, destBase, branch);
    }

    private static async Task WriteSkillVersionAsync(HttpClient http, string destBase, string branch)
    {
        try
        {
            var sha = await GetRemoteSkillCommitShaAsync(http, branch);
            if (sha == null) return;

            var versionInfo = new JsonObject
            {
                ["commit"] = sha,
                ["updatedAt"] = DateTime.UtcNow.ToString("o"),
                ["branch"] = branch
            };
            var versionPath = Path.Combine(destBase, ".skill-version");
            await File.WriteAllTextAsync(versionPath, CliJson.SerializeUntyped(versionInfo, indented: true));
        }
        catch { /* non-fatal — version tracking is best-effort */ }
    }

    private static async Task<string?> GetRemoteSkillCommitShaAsync(HttpClient http, string branch)
    {
        var url = $"https://api.github.com/repos/{SkillRepo}/commits?path={SkillBasePath}&sha={branch}&per_page=1";
        var json = await http.GetStringAsync(url);
        var commits = CliJson.ParseElement(json);
        foreach (var commit in commits.EnumerateArray())
            return commit.GetProperty("sha").GetString();
        return null;
    }

    private static async Task SkillVersionAsync(string? outputDir, string branch)
    {
        var root = outputDir ?? Directory.GetCurrentDirectory();
        var destBase = Path.Combine(root, SkillBasePath);
        var versionPath = Path.Combine(destBase, ".skill-version");

        // Read local version
        string? localSha = null;
        string? localDate = null;
        string? localBranch = null;
        if (File.Exists(versionPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(versionPath);
                var doc = CliJson.ParseElement(json);
                localSha = doc.TryGetProperty("commit", out var c) ? c.GetString() : null;
                localDate = doc.TryGetProperty("updatedAt", out var d) ? d.GetString() : null;
                localBranch = doc.TryGetProperty("branch", out var b) ? b.GetString() : null;
            }
            catch { /* corrupt file */ }
        }

        if (localSha == null)
        {
            Console.WriteLine("No local skill version found.");
            Console.WriteLine("Run 'maui-devflow update-skill' to install the skill and track its version.");
            return;
        }

        Console.WriteLine($"Installed: {localSha[..12]} (branch: {localBranch ?? "unknown"})");
        if (localDate != null && DateTime.TryParse(localDate, out var dt))
            Console.WriteLine($"Updated:   {dt:yyyy-MM-dd HH:mm:ss} UTC");

        // Check remote
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Microsoft.Maui.DevFlow-CLI", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            var remoteSha = await GetRemoteSkillCommitShaAsync(http, branch);
            if (remoteSha == null)
            {
                Console.WriteLine("Could not fetch remote version.");
                return;
            }

            Console.WriteLine($"Remote:    {remoteSha[..12]} (branch: {branch})");

            if (string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine("\n✓ Skill is up to date.");
            else
                Console.WriteLine("\n⚠ Update available! Run 'maui-devflow update-skill' to get the latest version.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not check remote: {ex.Message}");
        }
    }

    private static async Task<List<string>> GetSkillFilesFromGitHubAsync(HttpClient http, string branch)
    {
        var files = new List<string>();
        await ListGitHubDirectoryAsync(http, SkillBasePath, "", files, branch);
        return files;
    }

    private static async Task ListGitHubDirectoryAsync(HttpClient http, string basePath, string relativePath, List<string> files, string branch)
    {
        var apiPath = string.IsNullOrEmpty(relativePath) ? basePath : $"{basePath}/{relativePath}";
        var url = $"https://api.github.com/repos/{SkillRepo}/contents/{apiPath}?ref={branch}";
        var json = await http.GetStringAsync(url);
        var items = CliJson.ParseElement(json);

        foreach (var item in items.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var type = item.GetProperty("type").GetString()!;
            var itemRelative = string.IsNullOrEmpty(relativePath) ? name : $"{relativePath}/{name}";

            if (type == "file")
                files.Add(itemRelative);
            else if (type == "dir")
                await ListGitHubDirectoryAsync(http, basePath, itemRelative, files, branch);
        }
    }

    // ===== MAUI Agent Commands =====

    private static async Task MauiStatusAsync(string host, int port, bool json, int? window)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var status = await client.GetStatusAsync(window);
            if (status == null)
            {
                OutputWriter.WriteError($"Cannot connect to agent at {host}:{port}", json);
                _errorOccurred = true;
                return;
            }
            OutputWriter.WriteResult(status, json, s =>
            {
                Console.WriteLine($"Agent: {s.Agent} v{s.Version}");
                Console.WriteLine($"Platform: {s.Platform}");
                Console.WriteLine($"Device: {s.DeviceType} ({s.Idiom})");
                Console.WriteLine($"App: {s.AppName}");
            });
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiTreeAsync(string host, int port, bool json, int depth, int? window, string? fields, string? format)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var tree = await client.GetTreeAsync(depth, window);
            if (json)
            {
                var projected = ProjectElements(tree, fields, format);
                Console.WriteLine(CliJson.SerializeUntyped(projected, indented: true));
            }
            else
            {
                PrintTree(tree, 0);
            }
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiQueryAsync(string host, int port, bool json, string? type, string? autoId, string? text, string? selector, string? fields, string? format, string? waitUntil, int timeout)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);

            if (!string.IsNullOrWhiteSpace(waitUntil))
            {
                var condition = waitUntil.ToLowerInvariant();
                if (condition != "exists" && condition != "gone")
                {
                    OutputWriter.WriteError("--wait-until must be 'exists' or 'gone'", json, "InvocationError");
                    _errorOccurred = true;
                    return;
                }

                var deadline = DateTime.UtcNow.AddSeconds(timeout);
                var pollInterval = TimeSpan.FromMilliseconds(250);
                List<Microsoft.Maui.DevFlow.Driver.ElementInfo> results;

                while (true)
                {
                    results = !string.IsNullOrWhiteSpace(selector)
                        ? await client.QueryCssAsync(selector)
                        : await client.QueryAsync(type, autoId, text);

                    if (condition == "exists" && results.Count > 0) break;
                    if (condition == "gone" && results.Count == 0) break;
                    if (DateTime.UtcNow >= deadline)
                    {
                        OutputWriter.WriteError(
                            $"Timeout after {timeout}s: condition '{waitUntil}' not met",
                            json, "RuntimeError", retryable: true,
                            suggestions: new[] { "Increase --timeout", "Check element identifiers with 'MAUI tree'" });
                        _errorOccurred = true;
                        return;
                    }
                    await Task.Delay(pollInterval);
                }

                WriteQueryResults(results, json, fields, format);
                return;
            }

            List<Microsoft.Maui.DevFlow.Driver.ElementInfo> queryResults;

            if (!string.IsNullOrWhiteSpace(selector))
                queryResults = await client.QueryCssAsync(selector);
            else
                queryResults = await client.QueryAsync(type, autoId, text);

            WriteQueryResults(queryResults, json, fields, format);
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static void WriteQueryResults(List<Microsoft.Maui.DevFlow.Driver.ElementInfo> results, bool json, string? fields, string? format)
    {
        if (json)
        {
            var projected = ProjectElements(results, fields, format);
            Console.WriteLine(CliJson.SerializeUntyped(projected, indented: true));
        }
        else
        {
            if (results.Count == 0)
            {
                Console.WriteLine("No elements found");
                return;
            }
            Console.WriteLine($"Found {results.Count} element(s):");
            foreach (var el in results)
            {
                Console.WriteLine($"  [{el.Id}] {el.Type}" +
                    (el.AutomationId != null ? $" automationId=\"{el.AutomationId}\"" : "") +
                    (el.Text != null ? $" text=\"{el.Text}\"" : "") +
                    (el.IsVisible ? "" : " [hidden]") +
                    (el.IsEnabled ? "" : " [disabled]"));
            }
        }
    }

    private static async Task MauiHitTestAsync(string host, int port, bool json, double x, double y, int? window)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var result = await client.HitTestAsync(x, y, window);
            if (json)
                Console.WriteLine(result);
            else
                Console.WriteLine(result);
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiTapAsync(string host, int port, bool json, string elementId)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var success = await client.TapAsync(elementId);
            OutputWriter.WriteActionResult(success, "Tapped", elementId, json,
                success ? $"Tapped: {elementId}" : $"Failed to tap: {elementId}");
            if (!success) _errorOccurred = true;
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json, suggestions: new[] { "Run 'MAUI tree' to refresh element IDs" }); _errorOccurred = true; }
    }

    private static async Task MauiFillAsync(string host, int port, bool json, string elementId, string text)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var success = await client.FillAsync(elementId, text);
            OutputWriter.WriteActionResult(success, "Filled", elementId, json,
                success ? $"Filled: {elementId}" : $"Failed to fill: {elementId}");
            if (!success) _errorOccurred = true;
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json, suggestions: new[] { "Run 'MAUI tree' to refresh element IDs" }); _errorOccurred = true; }
    }

    private static async Task MauiClearAsync(string host, int port, bool json, string elementId)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var success = await client.ClearAsync(elementId);
            OutputWriter.WriteActionResult(success, "Cleared", elementId, json,
                success ? $"Cleared: {elementId}" : $"Failed to clear: {elementId}");
            if (!success) _errorOccurred = true;
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json, suggestions: new[] { "Run 'MAUI tree' to refresh element IDs" }); _errorOccurred = true; }
    }

    private static async Task MauiScreenshotAsync(string host, int port, bool json, string? output, int? window, string? id, string? selector, bool overwrite = false, int? maxWidth = null, string? scale = null)
    {
        try
        {
            var filename = output ?? $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            if (!overwrite && File.Exists(filename))
            {
                OutputWriter.WriteError($"File already exists: {Path.GetFullPath(filename)} (use --overwrite to replace)", json, "InvocationError");
                _errorOccurred = true;
                return;
            }

            byte[]? data = null;
            bool fromSimctl = false;

            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);

            // For full-screen captures (no element scoping), try simctl io screenshot first
            // when connected to an iOS simulator. This captures everything on the simulator
            // display including SpringBoard permission dialogs and system view controllers
            // that the in-app agent cannot see.
            if (id == null && selector == null && !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            {
                var status = await client.GetStatusAsync();
                if (status?.Platform?.Contains("iOS", StringComparison.OrdinalIgnoreCase) == true)
                {
                    data = await TrySimctlScreenshotAsync();
                    fromSimctl = data != null;
                }
            }

            // Fall back to agent-based screenshot (or used for element-scoped captures)
            if (data == null)
            {
                data = await client.ScreenshotAsync(window, id, selector, maxWidth, scale);
            }

            if (data == null)
            {
                OutputWriter.WriteError("Failed to capture screenshot", json);
                _errorOccurred = true;
                return;
            }

            // simctl screenshots are at native device resolution (e.g., 3x).
            // Apply the same auto-scaling the agent does: downscale to 1x logical pixels
            // unless scale=native was requested or an explicit maxWidth was given.
            if (fromSimctl)
                data = ResizeSimctlScreenshot(data, maxWidth, scale);

            await File.WriteAllBytesAsync(filename, data);
            var fullPath = Path.GetFullPath(filename);
            if (json)
            {
                OutputWriter.WriteResult(new JsonObject
                {
                    ["path"] = fullPath,
                    ["size"] = data.Length,
                    ["maxWidth"] = maxWidth,
                    ["scale"] = scale ?? "auto"
                }, json);
            }
            else
            {
                var target = id != null ? $" (element: {id})" : selector != null ? $" (selector: {selector})" : "";
                var scaleInfo = scale?.Equals("native", StringComparison.OrdinalIgnoreCase) == true ? " (native resolution)" :
                                maxWidth != null ? $" (max-width: {maxWidth}px)" : " (auto-scaled to 1x)";
                Console.WriteLine($"Screenshot saved: {fullPath} ({data.Length} bytes){target}{scaleInfo}");
            }
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    /// <summary>
    /// Attempts a simctl io screenshot for the booted iOS simulator.
    /// Returns PNG bytes on success, null on failure.
    /// Caller is responsible for checking platform before calling.
    /// </summary>
    private static async Task<byte[]?> TrySimctlScreenshotAsync()
    {
        try
        {
            var udid = await ResolveUdidAsync(null);

            var tempFile = Path.Combine(Path.GetTempPath(), $"devflow-simctl-{Guid.NewGuid():N}.png");
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("xcrun",
                    $"simctl io {udid} screenshot --type png \"{tempFile}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using var proc = System.Diagnostics.Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start xcrun");
                await proc.WaitForExitAsync();

                if (proc.ExitCode == 0 && File.Exists(tempFile))
                {
                    var bytes = await File.ReadAllBytesAsync(tempFile);
                    if (bytes.Length > 0)
                        return bytes;
                }
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
        catch
        {
            // Not a simulator, simctl unavailable, or UDID resolution failed — fall through
        }
        return null;
    }

    /// <summary>
    /// Downscales a simctl screenshot to match the agent's auto-scaling behavior.
    /// iOS simulator screenshots are at native device resolution (e.g., 3x on iPhone).
    /// By default, scales to 1x logical pixels. Respects scale=native and explicit maxWidth.
    /// </summary>
    private static byte[] ResizeSimctlScreenshot(byte[] pngData, int? maxWidth, string? scale)
    {
        // If scale=native was requested, return as-is
        if (scale != null && (scale.Equals("native", StringComparison.OrdinalIgnoreCase)
                           || scale.Equals("full", StringComparison.OrdinalIgnoreCase)))
            return pngData;

        try
        {
            using var original = SkiaSharp.SKBitmap.Decode(pngData);
            if (original == null) return pngData;

            // Determine target width: explicit maxWidth takes priority, then auto-scale by
            // the simulator's display scale (3x for modern iPhones, 2x for older/iPads).
            // We infer density from common iOS simulator resolutions.
            int? targetWidth = maxWidth;
            if (targetWidth == null)
            {
                double density = original.Width switch
                {
                    1290 or 1320 or 1206 => 3.0, // iPhone 14/15/16 Pro, Pro Max, standard
                    1170 => 3.0,                   // iPhone 12/13/14
                    1125 => 3.0,                   // iPhone X/XS/11 Pro
                    1242 => 3.0,                   // iPhone 8+/XS Max
                    828 => 2.0,                    // iPhone XR/11
                    750 => 2.0,                    // iPhone 8/SE
                    2048 or 2388 or 2360 => 2.0,   // iPad Pro/Air
                    _ => original.Width > 1000 ? 3.0 : 2.0 // default: assume 3x for large, 2x otherwise
                };
                targetWidth = (int)(original.Width / density);
            }

            if (targetWidth <= 0 || targetWidth >= original.Width)
                return pngData;

            var scaleRatio = (float)targetWidth.Value / original.Width;
            var newHeight = (int)(original.Height * scaleRatio);

            using var resized = original.Resize(
                new SkiaSharp.SKImageInfo(targetWidth.Value, newHeight),
                SkiaSharp.SKSamplingOptions.Default);
            if (resized == null) return pngData;

            using var image = SkiaSharp.SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
        catch
        {
            return pngData;
        }
    }

    private static async Task RecordingStartAsync(string host, int port, string platform, string? output, int timeout)
    {
        try
        {
            var filename = output ?? $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            using var driver = Microsoft.Maui.DevFlow.Driver.AppDriverFactory.Create(platform);
            await driver.StartRecordingAsync(filename, timeout);
            Console.WriteLine($"Recording started (timeout: {timeout}s)");
            Console.WriteLine($"Output: {Path.GetFullPath(filename)}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task RecordingStopAsync(string host, int port, string platform)
    {
        try
        {
            using var driver = Microsoft.Maui.DevFlow.Driver.AppDriverFactory.Create(platform);
            var outputFile = await driver.StopRecordingAsync();
            var size = File.Exists(outputFile) ? new FileInfo(outputFile).Length : 0;
            Console.WriteLine($"Recording saved: {outputFile} ({size} bytes)");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static void RecordingStatusAsync()
    {
        var state = Microsoft.Maui.DevFlow.Driver.RecordingStateManager.Load();
        if (state == null || !Microsoft.Maui.DevFlow.Driver.RecordingStateManager.IsRecording())
        {
            Console.WriteLine("No active recording.");
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - state.StartedAt;
        Console.WriteLine($"Recording in progress:");
        Console.WriteLine($"  Platform: {state.Platform}");
        Console.WriteLine($"  Output:   {state.OutputFile}");
        Console.WriteLine($"  Elapsed:  {elapsed.TotalSeconds:F0}s / {state.TimeoutSeconds}s");
        Console.WriteLine($"  PID:      {state.RecordingPid}");
    }

    private static async Task MauiPropertyAsync(string host, int port, bool json, string elementId, string propertyName)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var value = await client.GetPropertyAsync(elementId, propertyName);
            if (json)
            {
                OutputWriter.WriteResult(new JsonObject
                {
                    ["property"] = propertyName,
                    ["value"] = value
                }, json);
            }
            else
            {
                Console.WriteLine(value != null ? $"{propertyName}: {value}" : $"Property '{propertyName}' not found");
            }
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiSetPropertyAsync(string host, int port, bool json, string elementId, string propertyName, string value)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var success = await client.SetPropertyAsync(elementId, propertyName, value);
            if (success)
            {
                OutputWriter.WriteActionResult(true, "SetProperty", elementId, json,
                    $"Set {propertyName} = {value}");
            }
            else
            {
                OutputWriter.WriteError($"Failed to set {propertyName}", json);
                _errorOccurred = true;
            }
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiElementAsync(string host, int port, bool json, string elementId)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var el = await client.GetElementAsync(elementId);
            if (el == null)
            {
                OutputWriter.WriteError($"Element '{elementId}' not found", json,
                    suggestions: new[] { "Run 'MAUI tree' to refresh element IDs", "Element IDs are ephemeral — re-query after navigation" });
                _errorOccurred = true;
                return;
            }
            OutputWriter.WriteResult(el, json);
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiNavigateAsync(string host, int port, bool json, string route)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var success = await client.NavigateAsync(route);
            OutputWriter.WriteActionResult(success, "Navigated", route, json,
                success ? $"Navigated to: {route}" : $"Failed to navigate to: {route}");
            if (!success) _errorOccurred = true;
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiScrollAsync(string host, int port, bool json, string? elementId, double dx, double dy, bool animated, int? window, int? itemIndex = null, int? groupIndex = null, string? scrollToPosition = null)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var success = await client.ScrollAsync(elementId, dx, dy, animated, window, itemIndex, groupIndex, scrollToPosition);
            if (json)
            {
                OutputWriter.WriteActionResult(success, "Scrolled", elementId, json);
            }
            else
            {
                if (itemIndex.HasValue)
                    Console.WriteLine(success ? $"Scrolled to item index {itemIndex.Value}" : $"Failed to scroll to item index {itemIndex.Value}");
                else if (elementId != null)
                    Console.WriteLine(success ? $"Scrolled to element: {elementId}" : $"Failed to scroll to element: {elementId}");
                else
                    Console.WriteLine(success ? $"Scrolled by dx={dx}, dy={dy}" : "Failed to scroll");
            }
            if (!success) _errorOccurred = true;
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiFocusAsync(string host, int port, bool json, string elementId)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var success = await client.FocusAsync(elementId);
            OutputWriter.WriteActionResult(success, "Focused", elementId, json,
                success ? $"Focused: {elementId}" : $"Failed to focus: {elementId}");
            if (!success) _errorOccurred = true;
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiResizeAsync(string host, int port, bool json, int width, int height, int? window)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var success = await client.ResizeAsync(width, height, window);
            if (json)
                OutputWriter.WriteActionResult(success, "Resized", $"{width}x{height}", json);
            else
                Console.WriteLine(success ? $"Resized to: {width}x{height}" : $"Failed to resize");
            if (!success) _errorOccurred = true;
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiLogsAsync(string host, int port, bool json, int limit, int skip, string? source)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var body = await client.GetLogsAsync(limit, skip, source);

            if (json)
            {
                Console.WriteLine(body);
                return;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine(body);
                return;
            }

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                PrintLogEntry(entry);
            }
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiLogsFollowAsync(string host, int port, string? source, bool json, int replay)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(source))
            queryParams.Add($"source={Uri.EscapeDataString(source)}");
        if (replay != 100)
            queryParams.Add($"replay={replay}");
        var wsUrl = $"ws://{host}:{port}/ws/logs";
        if (queryParams.Count > 0)
            wsUrl += "?" + string.Join("&", queryParams);

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                using var ws = new System.Net.WebSockets.ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), cts.Token);

                if (!json)
                {
                    Console.WriteLine($"Connected to log stream at {host}:{port}");
                    if (!string.IsNullOrEmpty(source))
                        Console.WriteLine($"Filtering: source={source}");
                    Console.WriteLine("Streaming logs... (Ctrl+C to stop)\n");
                }

                var buffer = new byte[65536];
                var sb = new StringBuilder();

                while (!cts.Token.IsCancellationRequested && ws.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    System.Net.WebSockets.WebSocketReceiveResult result;
                    sb.Clear();
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                    if (cts.Token.IsCancellationRequested) break;

                    var msg = sb.ToString();
                    if (string.IsNullOrEmpty(msg)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(msg);
                        var type = doc.RootElement.GetProperty("type").GetString();

                        if (type == "replay" && doc.RootElement.TryGetProperty("entries", out var entries))
                        {
                            foreach (var entry in entries.EnumerateArray())
                            {
                                if (json)
                                    PrintLogEntryJson(entry);
                                else
                                    PrintLogEntry(entry);
                            }
                        }
                        else if (type == "log" && doc.RootElement.TryGetProperty("entry", out var logEntry))
                        {
                            if (json)
                                PrintLogEntryJson(logEntry);
                            else
                                PrintLogEntry(logEntry);
                        }
                    }
                    catch (JsonException) { }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (System.Net.WebSockets.WebSocketException)
            {
                if (cts.Token.IsCancellationRequested) break;
                try { await Task.Delay(1000, cts.Token); }
                catch { break; }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
                break;
            }
        }
    }

    private static void PrintLogEntry(JsonElement entry)
    {
        var ts = entry.TryGetProperty("t", out var tProp) ? tProp.GetString() ?? "" : "";
        var level = entry.TryGetProperty("l", out var lProp) ? lProp.GetString() ?? "" : "";
        var category = entry.TryGetProperty("c", out var cProp) ? cProp.GetString() ?? "" : "";
        var message = entry.TryGetProperty("m", out var mProp) ? mProp.GetString() ?? "" : "";
        var exception = entry.TryGetProperty("e", out var eProp) ? eProp.GetString() : null;
        var logSource = entry.TryGetProperty("s", out var sProp) ? sProp.GetString() : null;

        var color = level switch
        {
            "Critical" or "Error" => ConsoleColor.Red,
            "Warning" => ConsoleColor.Yellow,
            "Debug" or "Trace" => ConsoleColor.DarkGray,
            _ => ConsoleColor.White
        };

        var saved = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write($"[{ts}] ");
        Console.Write($"{level,-12} ");

        if (logSource == "webview")
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("[WebView] ");
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"{category}: ");
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        if (!string.IsNullOrEmpty(exception))
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine($"  Exception: {exception}");
        }
        Console.ForegroundColor = saved;
    }

    private static void PrintLogEntryJson(JsonElement entry)
    {
        Console.WriteLine(entry.GetRawText());
    }

    // ── Network monitoring ──

    private static async Task MauiNetworkMonitorAsync(string host, int port, bool json, int limit, string? filterHost, string? filterMethod)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        int counter = 0;
        var wsUrl = $"ws://{host}:{port}/ws/network";

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                using var ws = new System.Net.WebSockets.ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), cts.Token);

                if (!json)
                {
                    if (counter == 0)
                    {
                        Console.WriteLine($"Connected to network monitor at {host}:{port}");
                        Console.WriteLine("Listening for HTTP requests... (Ctrl+C to stop)\n");
                        PrintNetworkTableHeader();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("  (reconnected)");
                        Console.ResetColor();
                    }
                }

                var buffer = new byte[65536];
                var sb = new StringBuilder();

                while (!cts.Token.IsCancellationRequested && ws.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    System.Net.WebSockets.WebSocketReceiveResult result;
                    sb.Clear();
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                    if (cts.Token.IsCancellationRequested) break;

                    var msg = sb.ToString();
                    if (string.IsNullOrEmpty(msg)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(msg);
                        var type = doc.RootElement.GetProperty("type").GetString();

                        if (type == "replay" && doc.RootElement.TryGetProperty("entries", out var entries))
                        {
                            foreach (var entry in entries.EnumerateArray())
                            {
                                if (!MatchesFilter(entry, filterHost, filterMethod)) continue;
                                counter++;
                                if (json) PrintNetworkEntryJson(entry);
                                else PrintNetworkEntryRow(counter, entry);
                            }
                        }
                        else if (type == "request" && doc.RootElement.TryGetProperty("entry", out var reqEntry))
                        {
                            if (!MatchesFilter(reqEntry, filterHost, filterMethod)) continue;
                            counter++;
                            if (counter > limit) continue;
                            if (json) PrintNetworkEntryJson(reqEntry);
                            else PrintNetworkEntryRow(counter, reqEntry);
                        }
                    }
                    catch (JsonException) { }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (System.Net.WebSockets.WebSocketException)
            {
                if (cts.Token.IsCancellationRequested) break;
                // Reconnect after a brief delay
                try { await Task.Delay(1000, cts.Token); }
                catch { break; }
            }
            catch (Exception ex)
            {
                if (cts.Token.IsCancellationRequested) break;
                WriteError(ex.Message);
                try { await Task.Delay(2000, cts.Token); }
                catch { break; }
            }
        }

        if (!json && counter > 0) Console.WriteLine($"\n{counter} requests captured.");
    }

    private static async Task MauiNetworkListAsync(string host, int port, bool json, int limit, string? filterHost, string? filterMethod)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var requests = await client.GetNetworkRequestsAsync(limit, filterHost, filterMethod);

            if (json)
            {
                foreach (var r in requests)
                    Console.WriteLine(CliJson.SerializeUntyped(r, indented: false));
            }
            else
            {
                if (requests.Count == 0)
                {
                    Console.WriteLine("No network requests captured.");
                    return;
                }

                PrintNetworkTableHeader();
                int counter = 0;
                foreach (var r in requests)
                {
                    counter++;
                    PrintNetworkRequestRow(counter, r);
                }
                Console.WriteLine($"\n{requests.Count} requests.");
            }
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiNetworkDetailAsync(string host, int port, bool json, string id)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var req = await client.GetNetworkRequestDetailAsync(id);

            if (req == null)
            {
                OutputWriter.WriteError($"Network request '{id}' not found.", json);
                _errorOccurred = true;
                return;
            }

            if (json)
            {
                OutputWriter.WriteResult(req, json);
                return;
            }

            Console.WriteLine($"{"ID:",-20} {req.Id}");
            Console.WriteLine($"{"Timestamp:",-20} {req.Timestamp:O}");
            Console.WriteLine($"{"Method:",-20} {req.Method}");
            Console.WriteLine($"{"URL:",-20} {req.Url}");
            Console.WriteLine($"{"Status:",-20} {(req.StatusCode?.ToString() ?? "ERROR")} {req.StatusText ?? ""}");
            Console.WriteLine($"{"Duration:",-20} {req.DurationMs}ms");
            if (req.Error != null) Console.WriteLine($"{"Error:",-20} {req.Error}");

            if (req.RequestHeaders != null && req.RequestHeaders.Count > 0)
            {
                Console.WriteLine("\n── Request Headers ──");
                foreach (var h in req.RequestHeaders)
                    Console.WriteLine($"  {h.Key}: {string.Join(", ", h.Value)}");
            }

            if (req.RequestBody != null)
            {
                Console.WriteLine($"\n── Request Body ({req.RequestSize ?? 0} bytes{(req.RequestBodyTruncated ? ", truncated" : "")}) ──");
                PrintBody(req.RequestBody, req.RequestBodyEncoding);
            }

            if (req.ResponseHeaders != null && req.ResponseHeaders.Count > 0)
            {
                Console.WriteLine("\n── Response Headers ──");
                foreach (var h in req.ResponseHeaders)
                    Console.WriteLine($"  {h.Key}: {string.Join(", ", h.Value)}");
            }

            if (req.ResponseBody != null)
            {
                Console.WriteLine($"\n── Response Body ({req.ResponseSize ?? 0} bytes{(req.ResponseBodyTruncated ? ", truncated" : "")}) ──");
                PrintBody(req.ResponseBody, req.ResponseBodyEncoding);
            }
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task MauiNetworkClearAsync(string host, int port, bool json)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var result = await client.ClearNetworkRequestsAsync();
            OutputWriter.WriteActionResult(result, "NetworkCleared", null, json,
                result ? "Network request buffer cleared." : "Failed to clear.");
            if (!result) _errorOccurred = true;
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    // ── Network display helpers ──

    private static void PrintNetworkTableHeader()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"{"#",-5} {"Method",-7} {"URL",-50} {"Status",-7} {"Duration",-10} {"Size",-10}");
        Console.WriteLine(new string('─', 89));
        Console.ResetColor();
    }

    private static void PrintNetworkEntryRow(int index, JsonElement entry)
    {
        var method = entry.GetProperty("method").GetString() ?? "";
        var url = entry.GetProperty("url").GetString() ?? "";
        var statusCode = entry.TryGetProperty("statusCode", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32() : (int?)null;
        var durationMs = entry.TryGetProperty("durationMs", out var d) ? d.GetInt64() : 0;
        var responseSize = entry.TryGetProperty("responseSize", out var rs) && rs.ValueKind == JsonValueKind.Number ? rs.GetInt64() : (long?)null;
        var error = entry.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

        // Truncate URL
        if (url.Length > 48) url = url[..45] + "...";

        var statusStr = statusCode?.ToString() ?? (error != null ? "ERR" : "---");
        var durationStr = durationMs > 0 ? $"{durationMs}ms" : "--";
        var sizeStr = responseSize.HasValue ? FormatSize(responseSize.Value) : "--";

        Console.ForegroundColor = GetStatusColor(statusCode, error);
        Console.WriteLine($"{index,-5} {method,-7} {url,-50} {statusStr,-7} {durationStr,-10} {sizeStr,-10}");
        Console.ResetColor();
    }

    private static void PrintNetworkRequestRow(int index, Microsoft.Maui.DevFlow.Driver.NetworkRequest r)
    {
        var url = r.Url;
        if (url.Length > 48) url = url[..45] + "...";

        var statusStr = r.StatusCode?.ToString() ?? (r.Error != null ? "ERR" : "---");
        var durationStr = r.DurationMs > 0 ? $"{r.DurationMs}ms" : "--";
        var sizeStr = r.ResponseSize.HasValue ? FormatSize(r.ResponseSize.Value) : "--";

        Console.ForegroundColor = GetStatusColor(r.StatusCode, r.Error);
        Console.WriteLine($"{index,-5} {r.Method,-7} {url,-50} {statusStr,-7} {durationStr,-10} {sizeStr,-10}");
        Console.ResetColor();
    }

    private static void PrintNetworkEntryJson(JsonElement entry)
    {
        Console.WriteLine(entry.GetRawText());
    }

    private static void PrintBody(string body, string? encoding)
    {
        if (encoding == "base64")
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [base64, {body.Length} chars]");
            Console.ResetColor();
        }
        else
        {
            // Try to pretty-print JSON
            try
            {
                Console.WriteLine(CliJson.PrettyPrint(body));
            }
            catch
            {
                Console.WriteLine(body);
            }
        }
    }

    private static bool MatchesFilter(JsonElement entry, string? filterHost, string? filterMethod)
    {
        if (!string.IsNullOrEmpty(filterHost))
        {
            var host = entry.TryGetProperty("host", out var h) ? h.GetString() : null;
            if (host == null || !host.Contains(filterHost, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrEmpty(filterMethod))
        {
            var method = entry.GetProperty("method").GetString();
            if (!string.Equals(method, filterMethod, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static ConsoleColor GetStatusColor(int? statusCode, string? error)
    {
        if (error != null) return ConsoleColor.Red;
        return statusCode switch
        {
            >= 200 and < 300 => ConsoleColor.Green,
            >= 300 and < 400 => ConsoleColor.Yellow,
            >= 400 and < 500 => ConsoleColor.Red,
            >= 500 => ConsoleColor.DarkRed,
            _ => ConsoleColor.Gray
        };
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    private static void PrintTree(List<Microsoft.Maui.DevFlow.Driver.ElementInfo> elements, int indent)
    {
        foreach (var el in elements)
        {
            var prefix = new string(' ', indent * 2);
            var info = $"{prefix}[{el.Id}] {el.Type}";
            if (el.AutomationId != null) info += $" automationId=\"{el.AutomationId}\"";
            if (el.Text != null) info += $" text=\"{el.Text}\"";
            if (!el.IsVisible) info += " [hidden]";
            if (!el.IsEnabled) info += " [disabled]";
            if (el.Bounds != null) info += $" ({el.Bounds.X:F0},{el.Bounds.Y:F0} {el.Bounds.Width:F0}x{el.Bounds.Height:F0})";
            Console.WriteLine(info);
            if (el.Children != null)
                PrintTree(el.Children, indent + 1);
        }
    }

    /// <summary>
    /// Project elements to a subset of fields for reduced token usage.
    /// --format compact: only id, type, text, automationId, bounds, children
    /// --fields "id,type,text": only specified fields
    /// </summary>
    private static object ProjectElements(List<Microsoft.Maui.DevFlow.Driver.ElementInfo> elements, string? fields, string? format)
    {
        var isCompact = string.Equals(format, "compact", StringComparison.OrdinalIgnoreCase);
        HashSet<string>? fieldSet = null;

        if (!string.IsNullOrWhiteSpace(fields))
        {
            fieldSet = new HashSet<string>(
                fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
        }
        else if (isCompact)
        {
            fieldSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "id", "type", "text", "automationId", "bounds", "children", "isVisible", "isEnabled" };
        }

        if (fieldSet == null)
            return elements;

        var projected = new JsonArray();
        foreach (var element in elements)
            projected.Add((JsonNode)ProjectElement(element, fieldSet));

        return projected;
    }

    private static JsonObject ProjectElement(Microsoft.Maui.DevFlow.Driver.ElementInfo el, HashSet<string> fields)
    {
        var dict = new JsonObject();
        if (fields.Contains("id")) dict["id"] = el.Id;
        if (fields.Contains("parentId")) dict["parentId"] = el.ParentId;
        if (fields.Contains("type")) dict["type"] = el.Type;
        if (fields.Contains("fullType")) dict["fullType"] = el.FullType;
        if (fields.Contains("automationId") && el.AutomationId != null) dict["automationId"] = el.AutomationId;
        if (fields.Contains("text") && el.Text != null) dict["text"] = el.Text;
        if (fields.Contains("isVisible")) dict["isVisible"] = el.IsVisible;
        if (fields.Contains("isEnabled")) dict["isEnabled"] = el.IsEnabled;
        if (fields.Contains("isFocused")) dict["isFocused"] = el.IsFocused;
        if (fields.Contains("opacity")) dict["opacity"] = el.Opacity;
        if (fields.Contains("bounds") && el.Bounds != null)
        {
            dict["bounds"] = new JsonObject
            {
                ["x"] = el.Bounds.X,
                ["y"] = el.Bounds.Y,
                ["width"] = el.Bounds.Width,
                ["height"] = el.Bounds.Height
            };
        }
        if (fields.Contains("gestures") && el.Gestures != null)
        {
            var gestures = new JsonArray();
            foreach (var gesture in el.Gestures)
                gestures.Add((JsonNode?)JsonValue.Create(gesture));
            dict["gestures"] = gestures;
        }
        if (fields.Contains("nativeType")) dict["nativeType"] = el.NativeType;
        if (fields.Contains("nativeProperties") && el.NativeProperties != null)
        {
            var nativeProperties = new JsonObject();
            foreach (var (key, value) in el.NativeProperties)
                nativeProperties[key] = value;
            dict["nativeProperties"] = nativeProperties;
        }
        if (fields.Contains("children") && el.Children != null && el.Children.Count > 0)
        {
            var children = new JsonArray();
            foreach (var child in el.Children)
                children.Add((JsonNode)ProjectElement(child, fields));
            dict["children"] = children;
        }
        return dict;
    }

    // ===== Alert & Permission Commands (iOS Simulator) =====

    private static async Task<string> ResolveUdidAsync(string? udid)
    {
        if (!string.IsNullOrEmpty(udid)) return udid;

        // Auto-detect booted simulator
        var psi = new System.Diagnostics.ProcessStartInfo("xcrun", "simctl list devices booted -j")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        using var doc = JsonDocument.Parse(output);
        if (doc.RootElement.TryGetProperty("devices", out var devices))
        {
            foreach (var runtime in devices.EnumerateObject())
            {
                foreach (var device in runtime.Value.EnumerateArray())
                {
                    var state = device.TryGetProperty("state", out var s) ? s.GetString() : null;
                    if (state == "Booted")
                    {
                        var resolved = device.GetProperty("udid").GetString()!;
                        return resolved;
                    }
                }
            }
        }
        throw new InvalidOperationException("No booted simulator found. Specify --udid or boot a simulator.");
    }

    private static async Task<string> ResolveAlertPlatformAsync(string? udid, int? pid, string host, int port)
    {
        // If a UDID was explicitly provided, it's an iOS simulator
        if (!string.IsNullOrEmpty(udid))
            return "ios-simulator";

        // If a PID was explicitly provided, it's Mac Catalyst or Windows
        if (pid.HasValue)
            return OperatingSystem.IsWindows() ? "windows" : "maccatalyst";

        // Auto-detect from connected agent
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var status = await client.GetStatusAsync();
            if (status?.Platform != null)
            {
                var sp = status.Platform.ToLowerInvariant();
                if (sp.Contains("ios")) return "ios-simulator";
                if (sp.Contains("android")) return "android";
                if (sp.Contains("windows")) return "windows";
                if (sp.Contains("linux") || sp.Contains("gtk")) return "linux";
                // For MacCatalyst, don't return immediately — check if there's a
                // booted iOS simulator first, since it's more likely the user wants
                // iOS dialog detection (Mac Catalyst dialogs are less common)
            }
        }
        catch { }

        // Check if a booted iOS simulator exists
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("xcrun", "simctl list devices booted -j")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.TryGetProperty("devices", out var devices))
                {
                    foreach (var runtime in devices.EnumerateObject())
                    {
                        foreach (var device in runtime.Value.EnumerateArray())
                        {
                            var state = device.TryGetProperty("state", out var s) ? s.GetString() : null;
                            if (state == "Booted") return "ios-simulator";
                        }
                    }
                }
            }
            catch { }
        }

        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        return "maccatalyst";
    }

    private static async Task<int> ResolveMacCatalystPidAsync(int? pid, string host, int port)
    {
        if (pid.HasValue) return pid.Value;

        // Try to find the PID by checking what's listening on the agent port
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var status = await client.GetStatusAsync();
            if (status?.AppName != null)
            {
                // Find process by app name
                var psi = new System.Diagnostics.ProcessStartInfo("pgrep", $"-f {status.AppName}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0 && int.TryParse(lines[0].Trim(), out var resolved))
                    return resolved;
            }
        }
        catch { }

        throw new InvalidOperationException("Cannot determine Mac Catalyst app PID. Specify --pid.");
    }

    private static async Task<int> ResolveWindowsPidAsync(int? pid, string host, int port)
    {
        if (pid.HasValue) return pid.Value;

        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
            var status = await client.GetStatusAsync();
            if (status?.AppName != null)
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(status.AppName);
                if (processes.Length > 0)
                    return processes[0].Id;

                var match = System.Diagnostics.Process.GetProcesses()
                    .FirstOrDefault(p =>
                    {
                        try { return p.ProcessName.Contains(status.AppName, StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    });
                if (match != null)
                    return match.Id;
            }
        }
        catch { }

        throw new InvalidOperationException("Cannot determine Windows app PID. Specify --pid.");
    }

    private static async Task AlertDetectAsync(string? udid, int? pid, string host, int port, bool json)
    {
        try
        {
            var plat = await ResolveAlertPlatformAsync(udid, pid, host, port);
            Microsoft.Maui.DevFlow.Driver.AlertInfo? alert = null;

            if (plat == "maccatalyst")
            {
                var resolvedPid = await ResolveMacCatalystPidAsync(pid, host, port);
                var driver = new Microsoft.Maui.DevFlow.Driver.MacCatalystAppDriver { ProcessId = resolvedPid };
                alert = await driver.DetectAlertAsync();
            }
            else if (plat == "android")
            {
                var driver = new Microsoft.Maui.DevFlow.Driver.AndroidAppDriver { Serial = udid };
                alert = await driver.DetectAlertAsync();
            }
            else if (plat == "windows")
            {
                var resolvedPid = await ResolveWindowsPidAsync(pid, host, port);
                var driver = new Microsoft.Maui.DevFlow.Driver.WindowsAppDriver { ProcessId = resolvedPid };
                alert = await driver.DetectAlertAsync();
            }
            else
            {
                var resolved = await ResolveUdidAsync(udid);
                var driver = new Microsoft.Maui.DevFlow.Driver.iOSSimulatorAppDriver { DeviceUdid = resolved };
                alert = await driver.DetectAlertAsync();
            }

            if (alert is null)
            {
                OutputWriter.WriteResult(new JsonObject
                {
                    ["detected"] = false
                }, json, _ => Console.WriteLine("No alert detected"));
                return;
            }

            var buttons = new JsonArray();
            foreach (var button in alert.Buttons)
            {
                buttons.Add((JsonNode)new JsonObject
                {
                    ["label"] = button.Label,
                    ["centerX"] = button.CenterX,
                    ["centerY"] = button.CenterY
                });
            }

            OutputWriter.WriteResult(new JsonObject
            {
                ["detected"] = true,
                ["title"] = alert.Title,
                ["buttons"] = buttons
            }, json, _ =>
            {
                Console.WriteLine($"Alert: {alert.Title ?? "(no title)"}");
                foreach (var btn in alert.Buttons)
                    Console.WriteLine($"  Button: \"{btn.Label}\"");
            });
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task AlertDismissAsync(string? udid, int? pid, string host, int port, string? buttonLabel, bool json)
    {
        try
        {
            var plat = await ResolveAlertPlatformAsync(udid, pid, host, port);
            Microsoft.Maui.DevFlow.Driver.AlertInfo? alert = null;

            if (plat == "maccatalyst")
            {
                var resolvedPid = await ResolveMacCatalystPidAsync(pid, host, port);
                var driver = new Microsoft.Maui.DevFlow.Driver.MacCatalystAppDriver { ProcessId = resolvedPid };
                alert = await driver.HandleAlertIfPresentAsync(buttonLabel);
            }
            else if (plat == "android")
            {
                var driver = new Microsoft.Maui.DevFlow.Driver.AndroidAppDriver { Serial = udid };
                alert = await driver.HandleAlertIfPresentAsync(buttonLabel);
            }
            else if (plat == "windows")
            {
                var resolvedPid = await ResolveWindowsPidAsync(pid, host, port);
                var driver = new Microsoft.Maui.DevFlow.Driver.WindowsAppDriver { ProcessId = resolvedPid };
                alert = await driver.HandleAlertIfPresentAsync(buttonLabel);
            }
            else
            {
                var resolved = await ResolveUdidAsync(udid);
                var driver = new Microsoft.Maui.DevFlow.Driver.iOSSimulatorAppDriver { DeviceUdid = resolved };
                alert = await driver.HandleAlertIfPresentAsync(buttonLabel);
            }

            if (alert is null)
                OutputWriter.WriteResult(new JsonObject
                {
                    ["dismissed"] = false
                }, json, _ => Console.WriteLine("No alert to dismiss"));
            else
                OutputWriter.WriteResult(new JsonObject
                {
                    ["dismissed"] = true,
                    ["title"] = alert.Title
                }, json, _ => Console.WriteLine($"Dismissed: {alert.Title ?? "(alert)"}"));
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task AlertTreeAsync(string? udid, int? pid, string host, int port, bool json)
    {
        try
        {
            var plat = await ResolveAlertPlatformAsync(udid, pid, host, port);
            string treeResult;

            if (plat == "maccatalyst")
            {
                var resolvedPid = await ResolveMacCatalystPidAsync(pid, host, port);
                var driver = new Microsoft.Maui.DevFlow.Driver.MacCatalystAppDriver { ProcessId = resolvedPid };
                treeResult = await driver.GetAccessibilityTreeAsync();
            }
            else if (plat == "android")
            {
                var driver = new Microsoft.Maui.DevFlow.Driver.AndroidAppDriver { Serial = udid };
                treeResult = await driver.GetAccessibilityTreeAsync();
            }
            else if (plat == "windows")
            {
                var resolvedPid = await ResolveWindowsPidAsync(pid, host, port);
                var driver = new Microsoft.Maui.DevFlow.Driver.WindowsAppDriver { ProcessId = resolvedPid };
                treeResult = await driver.GetAccessibilityTreeAsync();
            }
            else
            {
                var resolved = await ResolveUdidAsync(udid);
                var driver = new Microsoft.Maui.DevFlow.Driver.iOSSimulatorAppDriver { DeviceUdid = resolved };
                treeResult = await driver.GetAccessibilityTreeAsync();
            }

            if (json)
            {
                // Try to parse as JSON and output directly; if not valid JSON, wrap as string
                try
                {
                    Console.WriteLine(CliJson.PrettyPrint(treeResult));
                }
                catch (JsonException)
                {
                    OutputWriter.WriteResult(new JsonObject
                    {
                        ["tree"] = treeResult
                    }, json);
                }
            }
            else
            {
                Console.WriteLine(treeResult);
            }
        }
        catch (Exception ex) { OutputWriter.WriteError(ex.Message, json); _errorOccurred = true; }
    }

    private static async Task PermissionAsync(string action, string? udid, string? bundleId, string service)
    {
        try
        {
            var resolved = await ResolveUdidAsync(udid);
            // Run xcrun simctl privacy directly (driver methods require BundleId which may not be set)
            var args = string.IsNullOrEmpty(bundleId)
                ? $"simctl privacy {resolved} {action} {service}"
                : $"simctl privacy {resolved} {action} {service} {bundleId}";

            var psi = new System.Diagnostics.ProcessStartInfo("xcrun", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                var stderr = await proc.StandardError.ReadToEndAsync();
                WriteError($"simctl privacy failed: {stderr.Trim()}");
                return;
            }
            Console.WriteLine($"Permission {action}: {service}" + (bundleId != null ? $" for {bundleId}" : ""));
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    /// <summary>
    /// Reads the port from .mauidevflow in the current directory.
    /// </summary>
    /// <summary>
    /// Resolves the agent port: broker discovery → .mauidevflow config → default 9223.
    /// </summary>
    private static int ResolveAgentPort()
    {
        try
        {
            var port = Broker.BrokerClient.ResolveAgentPortForProjectAsync().GetAwaiter().GetResult();
            if (port.HasValue) return port.Value;

            // No single match — check config file fallback
            var configPort = Broker.BrokerClient.ReadConfigPort();
            if (configPort.HasValue) return configPort.Value;

            // Multiple agents, can't disambiguate — show them so the caller
            // (human or AI agent) can re-run with --agent-port
            var brokerPort = Broker.BrokerClient.ReadBrokerPortPublic() ?? Broker.BrokerServer.DefaultPort;
            var agents = Broker.BrokerClient.ListAgentsAsync(brokerPort).GetAwaiter().GetResult();
            if (agents != null && agents.Length > 1)
            {
                Console.Error.WriteLine("Multiple agents connected. Use --agent-port to specify which one:");
                Console.Error.WriteLine();
                Console.Error.WriteLine($"{"ID",-15}{"App",-20}{"Platform",-15}{"TFM",-25}{"Port",-7}");
                Console.Error.WriteLine(new string('-', 82));
                foreach (var a in agents)
                    Console.Error.WriteLine($"{a.Id,-15}{a.AppName,-20}{a.Platform,-15}{a.Tfm,-25}{a.Port,-7}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Example: maui-devflow MAUI status --agent-port <port>");
            }
        }
        catch { /* broker unavailable, fall through */ }

        return Broker.BrokerClient.ReadConfigPort() ?? 9223;
    }

    // ===== Broker Commands =====

    private static async Task BrokerStartAsync(bool foreground)
    {
        if (foreground)
        {
            Console.CancelKeyPress += (_, e) => e.Cancel = true;
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, _) => cts.Cancel();

            using var server = new Broker.BrokerServer(
                log: msg => Console.WriteLine(msg));
            await server.RunAsync(cts.Token);
        }
        else
        {
            var port = await Broker.BrokerClient.EnsureBrokerRunningAsync();
            if (port.HasValue)
                Console.WriteLine($"Broker running on port {port.Value}");
            else
                Console.WriteLine("Failed to start broker");
        }
    }

    private static async Task BrokerStopAsync()
    {
        var success = await Broker.BrokerClient.ShutdownBrokerAsync();
        Console.WriteLine(success ? "Broker shutdown requested" : "Broker is not running");
    }

    private static async Task BrokerStatusAsync(bool json)
    {
        var port = Broker.BrokerClient.ReadBrokerPortPublic();

        // No state file — try default port as fallback (broker may still be alive)
        if (port == null)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var response = await http.GetStringAsync($"http://localhost:{Broker.BrokerServer.DefaultPort}/api/health");
                var doc = JsonDocument.Parse(response);
                var agents = doc.RootElement.GetProperty("agents").GetInt32();
                OutputWriter.WriteResult(new JsonObject
                {
                    ["running"] = true,
                    ["port"] = Broker.BrokerServer.DefaultPort,
                    ["agents"] = agents,
                    ["stateFile"] = false
                }, json,
                    _ => Console.WriteLine($"Broker: running on port {Broker.BrokerServer.DefaultPort} ({agents} agent(s) connected) [no state file]"));
                return;
            }
            catch { }

            OutputWriter.WriteResult(new JsonObject
            {
                ["running"] = false
            }, json,
                _ => Console.WriteLine("Broker: not running"));
            return;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await http.GetStringAsync($"http://localhost:{port}/api/health");
            var doc = JsonDocument.Parse(response);
            var agents = doc.RootElement.GetProperty("agents").GetInt32();
            OutputWriter.WriteResult(new JsonObject
            {
                ["running"] = true,
                ["port"] = port,
                ["agents"] = agents
            }, json,
                _ => Console.WriteLine($"Broker: running on port {port} ({agents} agent(s) connected)"));
        }
        catch
        {
            OutputWriter.WriteResult(new JsonObject
            {
                ["running"] = false,
                ["port"] = port,
                ["stale"] = true
            }, json,
                _ => Console.WriteLine($"Broker: not responding on port {port} (stale state file?)"));
        }
    }

    private static void BrokerLogAsync()
    {
        var logPath = Broker.BrokerPaths.LogFile;
        if (!File.Exists(logPath))
        {
            Console.WriteLine("No broker log found");
            return;
        }

        // Show last 50 lines
        var lines = File.ReadAllLines(logPath);
        var start = Math.Max(0, lines.Length - 50);
        for (int i = start; i < lines.Length; i++)
            Console.WriteLine(lines[i]);
    }

    private static async Task ListAgentsCommandAsync(bool json)
    {
        var port = await Broker.BrokerClient.EnsureBrokerRunningAsync();
        if (port == null)
        {
            OutputWriter.WriteError("Broker unavailable", json);
            _errorOccurred = true;
            return;
        }

        var agents = await Broker.BrokerClient.ListAgentsAsync(port.Value);
        if (agents == null || agents.Length == 0)
        {
            // Scan current directory for devflow-enabled projects
            var projects = ScanForDevFlowProjects();
            
            if (json)
            {
                var result = new { agents = Array.Empty<object>(), projects };
                OutputWriter.WriteResult(result, json);
            }
            else
            {
                Console.WriteLine("No agents connected.");
                
                if (projects.Length > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("DevFlow-enabled projects found:");
                    foreach (var proj in projects)
                    {
                        Console.WriteLine($"  📦 {proj}");
                    }
                    Console.WriteLine();
                    Console.WriteLine("Hint: Launch your app in Debug mode, then run 'maui-devflow wait'");
                }
                else
                {
                    Console.WriteLine("No DevFlow-enabled projects found in current directory.");
                }
            }
            return;
        }

        if (json)
        {
            OutputWriter.WriteResult(agents, json);
        }
        else
        {
            Console.WriteLine($"{"ID",-14} {"App",-20} {"Platform",-14} {"TFM",-24} {"Port",-6} {"Version",-12} {"Uptime"}");
            Console.WriteLine(new string('-', 102));
            foreach (var a in agents)
            {
                var uptime = DateTime.UtcNow - a.ConnectedAt;
                var uptimeStr = uptime.TotalHours >= 1
                    ? $"{uptime.Hours}h {uptime.Minutes}m"
                    : $"{uptime.Minutes}m {uptime.Seconds}s";
                var version = a.Version ?? "-";
                Console.WriteLine($"{a.Id,-14} {a.AppName,-20} {a.Platform,-14} {a.Tfm,-24} {a.Port,-6} {version,-12} {uptimeStr}");
            }
        }
    }

    private static async Task DiagnoseCommandAsync(bool json)
    {
        var diagnostics = new Dictionary<string, object>();
        
        // Get CLI version
        var version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        diagnostics["cli_version"] = version;
        
        // Check broker status
        var brokerPort = await Broker.BrokerClient.EnsureBrokerRunningAsync();
        var brokerRunning = brokerPort != null;
        diagnostics["broker_running"] = brokerRunning;
        if (brokerRunning)
            diagnostics["broker_port"] = brokerPort!.Value;
        
        // List connected agents
        var agents = brokerRunning ? await Broker.BrokerClient.ListAgentsAsync(brokerPort!.Value) : null;
        var agentCount = agents?.Length ?? 0;
        diagnostics["agent_count"] = agentCount;
        diagnostics["agents"] = agents ?? Array.Empty<object>();
        
        // Scan for devflow-enabled projects
        var projects = ScanForDevFlowProjects();
        diagnostics["projects"] = projects;
        
        if (json)
        {
            OutputWriter.WriteResult(diagnostics, json);
            return;
        }
        
        // Human-readable output
        Console.WriteLine("DevFlow Diagnostics");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"✅ CLI version:     {version}");
        
        if (brokerRunning)
        {
            Console.WriteLine($"✅ Broker:          Running on port {brokerPort} ({agentCount} agent(s) connected)");
        }
        else
        {
            Console.WriteLine($"❌ Broker:          Not running → Run 'maui-devflow broker start'");
        }
        
        Console.WriteLine();
        
        if (agentCount > 0)
        {
            Console.WriteLine("✅ Connected agents:");
            foreach (var agent in agents!)
            {
                var uptime = DateTime.UtcNow - agent.ConnectedAt;
                var totalHours = (int)uptime.TotalHours;
                var uptimeStr = uptime.TotalHours >= 1
                    ? $"{totalHours}h {uptime.Minutes}m"
                    : $"{uptime.Minutes}m {uptime.Seconds}s";
                Console.WriteLine($"   • {agent.AppName} ({agent.Platform}, port {agent.Port}, uptime {uptimeStr})");
            }
        }
        else
        {
            Console.WriteLine("⚠️  No agents connected");
        }
        
        Console.WriteLine();
        
        if (projects.Length > 0)
        {
            Console.WriteLine("📦 DevFlow-enabled projects:");
            foreach (var proj in projects)
            {
                Console.WriteLine($"   • {proj}");
            }
            
            if (agentCount == 0)
            {
                Console.WriteLine();
                Console.WriteLine("💡 Suggestion: Your project has DevFlow but no agent is connected.");
                Console.WriteLine("   1. Ensure the app is running in Debug configuration");
                Console.WriteLine($"   2. Run: maui-devflow wait --project \"{projects[0].Replace("\"", "\\\"")}\"");
            }
        }
        else
        {
            Console.WriteLine("📦 DevFlow-enabled projects: (none found in current directory)");
        }
    }

    private static async Task WaitForAgentCommandAsync(int timeoutSeconds, string? projectFilter, string? platformFilter, bool json)
    {
        var brokerPort = await Broker.BrokerClient.EnsureBrokerRunningAsync();
        if (brokerPort == null)
        {
            Console.Error.WriteLine("Error: Broker unavailable");
            Environment.ExitCode = 1;
            return;
        }

        // Resolve project filter to full path for matching
        string? resolvedProject = null;
        if (projectFilter != null)
        {
            resolvedProject = Path.GetFullPath(projectFilter);
        }

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var pollInterval = TimeSpan.FromMilliseconds(500);
        Broker.AgentRegistration? matched = null;

        while (DateTime.UtcNow < deadline)
        {
            var agents = await Broker.BrokerClient.ListAgentsAsync(brokerPort.Value);
            if (agents != null && agents.Length > 0)
            {
                matched = FindMatchingAgent(agents, resolvedProject, platformFilter);
                if (matched != null)
                    break;
            }

            await Task.Delay(pollInterval);
        }

        if (matched == null)
        {
            Console.Error.WriteLine($"Timeout: no matching agent connected within {timeoutSeconds}s");
            Environment.ExitCode = 1;
            return;
        }

        if (json)
        {
            Console.WriteLine(CliJson.SerializeUntyped(matched, indented: false));
        }
        else
        {
            Console.WriteLine(matched.Port);
        }
    }

    private static Broker.AgentRegistration? FindMatchingAgent(Broker.AgentRegistration[] agents, string? projectFilter, string? platformFilter)
    {
        foreach (var agent in agents)
        {
            if (projectFilter != null && !string.Equals(agent.Project, projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (platformFilter != null && !agent.Platform.Contains(platformFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            return agent;
        }
        return null;
    }

    // ===== Batch command: interactive stdin/stdout with JSONL responses =====

    private static async Task BatchAsync(string host, int port, int delayMs, bool continueOnError, bool human)
    {
        var commandIndex = 0;
        var succeeded = 0;
        var failed = 0;
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        using var stdin = Console.In;
        string? line;
        while ((line = stdin.ReadLine()) != null)
        {
            var commands = SplitBatchLine(line);
            foreach (var rawCmd in commands)
            {
                commandIndex++;
                var args = TokenizeCommand(rawCmd);
                if (args.Length == 0) continue;

                var prefix = args[0].ToUpperInvariant();
                if (prefix != "MAUI" && prefix != "CDP")
                {
                    var errMsg = $"Only MAUI and cdp commands are supported in batch mode, got: {args[0]}";
                    if (human)
                    {
                        originalOut.WriteLine($"[{commandIndex}] {rawCmd}");
                        originalErr.WriteLine($"Error: {errMsg}");
                    }
                    else
                    {
                        var errJson = CliJson.SerializeUntyped(new JsonObject
                        {
                            ["command"] = rawCmd,
                            ["exit_code"] = 1,
                            ["output"] = $"Error: {errMsg}"
                        }, indented: false);
                        originalOut.WriteLine(errJson);
                        originalOut.Flush();
                    }
                    failed++;
                    if (!continueOnError) goto done;
                    continue;
                }

                // Inject resolved port/host so sub-commands don't re-query broker
                var fullArgs = new List<string>(args) { "--agent-port", port.ToString(), "--agent-host", host };

                // Capture stdout/stderr from the sub-command
                var outCapture = new StringWriter();
                var errCapture = new StringWriter();
                Console.SetOut(outCapture);
                Console.SetError(errCapture);

                _errorOccurred = false;
                int exitCode;
                try
                {
                    exitCode = await _parser!.InvokeAsync(fullArgs.ToArray());
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalErr);
                }
                if (_errorOccurred) exitCode = 1;

                var output = outCapture.ToString().TrimEnd('\r', '\n');
                var errOutput = errCapture.ToString().TrimEnd('\r', '\n');
                var combinedOutput = string.IsNullOrEmpty(errOutput) ? output : $"{output}\n{errOutput}".TrimStart('\n');

                if (exitCode == 0) succeeded++;
                else failed++;

                if (human)
                {
                    originalOut.WriteLine($"[{commandIndex}] {rawCmd}");
                    if (!string.IsNullOrEmpty(combinedOutput))
                        originalOut.WriteLine(combinedOutput);
                }
                else
                {
                    var jsonResponse = CliJson.SerializeUntyped(new JsonObject
                    {
                        ["command"] = rawCmd,
                        ["exit_code"] = exitCode,
                        ["output"] = combinedOutput
                    }, indented: false);
                    originalOut.WriteLine(jsonResponse);
                    originalOut.Flush();
                }

                if (exitCode != 0 && !continueOnError) goto done;

                if (delayMs > 0)
                    await Task.Delay(delayMs);
            }
        }

    done:
        if (human)
        {
            originalOut.WriteLine();
            if (failed == 0)
                originalOut.WriteLine($"Batch complete: {succeeded}/{succeeded + failed} commands succeeded");
            else
                originalOut.WriteLine($"Batch stopped: {succeeded}/{succeeded + failed} commands succeeded, {failed} failed");
        }
    }

    private static List<string> SplitBatchLine(string line)
    {
        var commands = new List<string>();
        var inQuote = false;
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') { inQuote = !inQuote; current.Append(c); }
            else if (c == ';' && !inQuote)
            {
                var cmd = current.ToString().Trim();
                if (cmd.Length > 0 && !cmd.StartsWith('#'))
                    commands.Add(cmd);
                current.Clear();
            }
            else { current.Append(c); }
        }

        var last = current.ToString().Trim();
        if (last.Length > 0 && !last.StartsWith('#'))
            commands.Add(last);

        return commands;
    }

    private static string[] TokenizeCommand(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;

        for (int i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (c == '"')
            {
                inQuote = !inQuote;
            }
            else if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens.ToArray();
    }

    /// <summary>
    /// Scans the current directory for csproj files containing DevFlow package references.
    /// Returns relative paths to projects that have DevFlow enabled.
    /// </summary>
    private static readonly HashSet<string> _scanExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".idea", "packages"
    };

    private static string[] ScanForDevFlowProjects()
    {
        var cwd = Directory.GetCurrentDirectory();
        var projects = new List<string>();

        try
        {
            var pending = new Queue<string>();
            pending.Enqueue(cwd);

            while (pending.Count > 0)
            {
                var dir = pending.Dequeue();

                // Search for .csproj files in this directory only (not recursive)
                try
                {
                    foreach (var csproj in Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var content = File.ReadAllText(csproj);
                            if (content.Contains("Redth.MauiDevFlow.Agent") ||
                                content.Contains("Microsoft.Maui.DevFlow.Agent"))
                            {
                                projects.Add(Path.GetRelativePath(cwd, csproj));
                            }
                        }
                        catch
                        {
                            // Skip files we can't read
                        }
                    }
                }
                catch
                {
                    // Skip directories we can't enumerate
                    continue;
                }

                // Enqueue subdirectories, pruning excluded ones before recursing
                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        var dirName = Path.GetFileName(subDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (!string.IsNullOrEmpty(dirName) && !_scanExcludedDirs.Contains(dirName))
                            pending.Enqueue(subDir);
                    }
                }
                catch
                {
                    // Skip directories we can't enumerate
                }
            }
        }
        catch
        {
            // If scanning fails, return empty array
        }

        projects.Sort(StringComparer.OrdinalIgnoreCase);
        return projects.ToArray();
    }
}
