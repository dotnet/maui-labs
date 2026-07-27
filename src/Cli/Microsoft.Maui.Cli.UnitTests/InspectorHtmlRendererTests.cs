using System.Globalization;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class InspectorHtmlRendererTests
{
    [Fact]
    public void RenderElements_EscapesHtmlInTextAttribute()
    {
        var tree = new List<ElementInfo>
        {
            new()
            {
                Id = "e1",
                Type = "Label",
                Text = "<script>alert(1)</script>",
                Bounds = new BoundsInfo { X = 0, Y = 0, Width = 10, Height = 10 },
            },
        };

        var html = HtmlRenderer.RenderElements(tree);

        // HtmlAttributeEncode encodes <, &, " (but not >). The key invariant is
        // that raw "<script" cannot appear because that's what would break out of
        // an attribute context and into HTML.
        Assert.DoesNotContain("<script", html);
        Assert.Contains("&lt;script", html);
    }

    [Fact]
    public void RenderElements_FormatsOpacityUsingInvariantCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-PT"); // comma decimal
            var tree = new List<ElementInfo>
            {
                new()
                {
                    Id = "e1",
                    Type = "Label",
                    Opacity = 0.5,
                    Bounds = new BoundsInfo { X = 0, Y = 0, Width = 10, Height = 10 },
                },
            };

            var html = HtmlRenderer.RenderElements(tree);

            Assert.Contains("data-opacity=\"0.5\"", html);
            Assert.DoesNotContain("data-opacity=\"0,5\"", html);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void RenderElements_FormatsBoundsUsingInvariantCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-PT"); // comma decimal

            // Use a fractional scale so the F0 formatter would actually emit a decimal
            // separator under a comma-decimal culture if InvariantCulture were not used.
            var html = HtmlRenderer.RenderElements(new List<ElementInfo>
            {
                new()
                {
                    Id = "e1",
                    Type = "Label",
                    Opacity = 0.25,
                    Bounds = new BoundsInfo { X = 1.5, Y = 2.5, Width = 10.5, Height = 20.5 },
                },
            }, elementScale: 1.5);

            Assert.Contains("position:absolute", html);
            Assert.Contains("data-opacity=\"0.25\"", html);
            Assert.DoesNotContain("data-opacity=\"0,25\"", html);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void RenderElements_SubtractsRootOffset_ForModalCoordinateSpace()
    {
        // Simulates a modal page at WindowBounds (0, 100) — the screenshot is
        // captured from the modal's origin, so overlays must subtract the offset.
        var tree = new List<ElementInfo>
        {
            new()
            {
                Id = "modal_btn",
                Type = "Button",
                WindowBounds = new BoundsInfo { X = 20, Y = 150, Width = 100, Height = 40 },
            },
        };

        // With rootOffsetY = 100, the button at Y=150 should render at top:50px
        var html = HtmlRenderer.RenderElements(tree, elementScale: 1, rootOffsetX: 0, rootOffsetY: 100);

        Assert.Contains("top:50px", html);
        Assert.Contains("left:20px", html);
    }

    [Fact]
    public void RenderElements_ZeroOffset_PreservesOriginalCoordinates()
    {
        var tree = new List<ElementInfo>
        {
            new()
            {
                Id = "btn",
                Type = "Button",
                WindowBounds = new BoundsInfo { X = 30, Y = 80, Width = 100, Height = 40 },
            },
        };

        // With zero offset (default), coordinates pass through unchanged
        var html = HtmlRenderer.RenderElements(tree);

        Assert.Contains("top:80px", html);
        Assert.Contains("left:30px", html);
    }

    [Fact]
    public void RenderElements_RedactedPasswordEntry_IsMarkedSensitive()
    {
        var tree = new List<ElementInfo>
        {
            new()
            {
                Id = "password",
                Type = "Entry",
                Text = "[REDACTED]",
                Bounds = new BoundsInfo { X = 0, Y = 0, Width = 100, Height = 30 },
            },
        };

        var html = HtmlRenderer.RenderElements(tree);

        Assert.Contains("data-sensitive=\"true\"", html);
    }

    [Fact]
    public void RenderElements_NativePasswordElement_IsMarkedSensitive()
    {
        var tree = new List<ElementInfo>
        {
            new()
            {
                Id = "native-password",
                Type = "TextBox",
                Value = "[REDACTED]",
                NativeProperties = new Dictionary<string, string?>
                {
                    ["isPassword"] = "True",
                },
                Bounds = new BoundsInfo { X = 0, Y = 0, Width = 100, Height = 30 },
            },
        };

        var html = HtmlRenderer.RenderElements(tree);

        Assert.Contains("data-sensitive=\"true\"", html);
    }
}
