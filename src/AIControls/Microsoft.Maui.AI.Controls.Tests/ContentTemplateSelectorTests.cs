// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Controls.Templates;

namespace Microsoft.Maui.AI.Controls.Tests;

public class ContentTemplateSelectorTests
{
    [Fact]
    public void TextContent_ReturnsTextTemplate()
    {
        var selector = new ContentTemplateSelector
        {
            TextContentTemplate = new DataTemplate(),
            DefaultContentTemplate = new DataTemplate()
        };

        var result = selector.SelectTemplate(new TextContent("hello"), null!);
        Assert.Same(selector.TextContentTemplate, result);
    }

    [Fact]
    public void FunctionCallContent_ReturnsFunctionCallTemplate()
    {
        var selector = new ContentTemplateSelector
        {
            FunctionCallContentTemplate = new DataTemplate(),
            DefaultContentTemplate = new DataTemplate()
        };

        var fcc = new FunctionCallContent("call1", "get_weather", new Dictionary<string, object?> { ["city"] = "Tokyo" });
        var result = selector.SelectTemplate(fcc, null!);
        Assert.Same(selector.FunctionCallContentTemplate, result);
    }

    [Fact]
    public void FunctionCallContent_WithMatchingFunctionTemplate_ReturnsFunctionSpecificTemplate()
    {
        var weatherTemplate = new DataTemplate();
        var selector = new ContentTemplateSelector
        {
            FunctionCallContentTemplate = new DataTemplate(),
            DefaultContentTemplate = new DataTemplate()
        };
        selector.FunctionTemplates["get_weather"] = weatherTemplate;

        var fcc = new FunctionCallContent("call1", "get_weather", new Dictionary<string, object?> { ["city"] = "Tokyo" });
        var result = selector.SelectTemplate(fcc, null!);
        Assert.Same(weatherTemplate, result);
    }

    [Fact]
    public void FunctionCallContent_WithNonMatchingFunctionTemplate_ReturnsFunctionCallTemplate()
    {
        var weatherTemplate = new DataTemplate();
        var selector = new ContentTemplateSelector
        {
            FunctionCallContentTemplate = new DataTemplate(),
            DefaultContentTemplate = new DataTemplate()
        };
        selector.FunctionTemplates["get_weather"] = weatherTemplate;

        var fcc = new FunctionCallContent("call1", "calculate", new Dictionary<string, object?> { ["expr"] = "1+1" });
        var result = selector.SelectTemplate(fcc, null!);
        Assert.Same(selector.FunctionCallContentTemplate, result);
    }

    [Fact]
    public void FunctionResultContent_ReturnsFunctionResultTemplate()
    {
        var selector = new ContentTemplateSelector
        {
            FunctionResultContentTemplate = new DataTemplate(),
            DefaultContentTemplate = new DataTemplate()
        };

        var frc = new FunctionResultContent("call1", "result");
        var result = selector.SelectTemplate(frc, null!);
        Assert.Same(selector.FunctionResultContentTemplate, result);
    }

    [Fact]
    public void DataContent_ReturnsDataContentTemplate()
    {
        var selector = new ContentTemplateSelector
        {
            DataContentTemplate = new DataTemplate(),
            DefaultContentTemplate = new DataTemplate()
        };

        var dc = new DataContent(new byte[] { 1, 2, 3 }, "application/json");
        var result = selector.SelectTemplate(dc, null!);
        Assert.Same(selector.DataContentTemplate, result);
    }

    [Fact]
    public void UnknownContent_ReturnsDefaultTemplate()
    {
        var selector = new ContentTemplateSelector
        {
            TextContentTemplate = new DataTemplate(),
            DefaultContentTemplate = new DataTemplate()
        };

        // Use a type that doesn't match any specific template
        var content = new UriContent(new Uri("https://example.com"), "text/html");
        var result = selector.SelectTemplate(content, null!);
        Assert.Same(selector.DefaultContentTemplate, result);
    }

    [Fact]
    public void NullTemplates_ReturnNewDataTemplate()
    {
        var selector = new ContentTemplateSelector();
        var result = selector.SelectTemplate(new TextContent("test"), null!);
        Assert.NotNull(result);
    }
}
