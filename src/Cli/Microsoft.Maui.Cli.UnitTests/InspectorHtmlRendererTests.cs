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

    private static ElementInfo Overlay(string id, string type, double x, double y, double width, double height, params ElementInfo[] children) => new()
    {
        Id = id,
        Type = type,
        IsVisible = true,
        WindowBounds = new BoundsInfo { X = x, Y = y, Width = width, Height = height },
        Children = children.Length > 0 ? children.ToList() : null,
    };

    // RenderElements emits one div per line.
    private static string OverlayDiv(string html, string id)
        => html.Split('\n').Single(line => line.Contains($"data-id=\"{id}\""));

    [Theory]
    // Mac Catalyst: the closed flyout row is realized at x = -320 with its right edge at 0.
    [InlineData(-320, 320, -270, 270)]
    // iOS: fractional bounds put the right edge at +0.1.
    [InlineData(-321.6, 321.7, -271.6, 271.7)]
    public void RenderElements_ClosedShellFlyoutItems_AreMarkedOffscreenWhileKeepingAgentVisibility(
        double rowX, double rowWidth, double labelX, double labelWidth)
    {
        var tree = new List<ElementInfo>
        {
            Overlay("shell", "Shell", 0, 0, 400, 800,
                Overlay("page", "ContentPage", 0, 0, 400, 800),
                Overlay("flyout-row", "Grid", rowX, 100, rowWidth, 50,
                    Overlay("flyout-label", "Label", labelX, 110, labelWidth, 30))),
        };

        var html = HtmlRenderer.RenderElements(tree, viewportWidth: 400, viewportHeight: 800);

        Assert.Contains("data-offscreen=\"true\"", OverlayDiv(html, "flyout-row"));
        Assert.Contains("data-offscreen=\"true\"", OverlayDiv(html, "flyout-label"));
        // The Agent's own displayed value is passed through untouched.
        Assert.Contains("data-isVisible=\"true\"", OverlayDiv(html, "flyout-row"));
        Assert.DoesNotContain("data-offscreen", OverlayDiv(html, "shell"));
        Assert.DoesNotContain("data-offscreen", OverlayDiv(html, "page"));
    }

    [Theory]
    [InlineData(0, 0, 400, 800, false)]     // fills the viewport
    [InlineData(-50, 10, 100, 30, false)]   // straddles the left edge
    [InlineData(380, 10, 100, 30, false)]   // straddles the right edge
    [InlineData(10, -20, 100, 30, false)]   // straddles the top edge
    [InlineData(10, 790, 100, 30, false)]   // straddles the bottom edge
    [InlineData(-49, 10, 50, 30, false)]    // 1px sliver inside the left edge
    [InlineData(399, 10, 50, 30, false)]    // 1px sliver inside the right edge
    [InlineData(10, 10, 0, 30, false)]      // zero-width element inside the viewport
    [InlineData(-320, 10, 320, 30, true)]   // right edge exactly at the left edge
    [InlineData(-321.6, 10, 321.7, 30, true)] // right edge 0.1 inside the left edge
    [InlineData(400, 10, 100, 30, true)]    // starts at the right edge
    [InlineData(10, -30, 100, 30, true)]    // bottom edge at the top edge
    [InlineData(10, 800, 100, 30, true)]    // starts at the bottom edge
    public void RenderElements_MarksOffscreenOnlyWhenNoPartIsInsideTheViewport(
        double x, double y, double width, double height, bool expectedOffscreen)
    {
        var html = HtmlRenderer.RenderElements(
            [Overlay("target", "Grid", x, y, width, height)], viewportWidth: 400, viewportHeight: 800);

        Assert.Equal(expectedOffscreen, OverlayDiv(html, "target").Contains("data-offscreen=\"true\""));
    }

    [Fact]
    public void RenderElements_WithoutViewport_NeverMarksOffscreen()
    {
        var html = HtmlRenderer.RenderElements([Overlay("flyout-row", "Grid", -320, 100, 320, 50)]);

        Assert.DoesNotContain("data-offscreen", html);
    }

    [Fact]
    public void RenderElements_ChecksOffscreenAfterApplyingRootOffset()
    {
        // The modal screenshot starts at window y = 100, so "above" falls outside it even
        // though its window y (50) is inside the window.
        var html = HtmlRenderer.RenderElements(
            [Overlay("inside", "Button", 20, 150, 100, 40), Overlay("above", "Button", 20, 50, 100, 40)],
            rootOffsetY: 100, viewportWidth: 400, viewportHeight: 800);

        Assert.DoesNotContain("data-offscreen", OverlayDiv(html, "inside"));
        Assert.Contains("data-offscreen=\"true\"", OverlayDiv(html, "above"));
    }

    [Fact]
    public void Render_MarksOffscreenAgainstTheScreenshotSize()
    {
        var html = HtmlRenderer.Render(
            [Overlay("page", "ContentPage", 0, 0, 400, 800), Overlay("flyout-row", "Grid", -320, 100, 320, 50)],
            hasScreenshot: true, screenshotWidth: 400, screenshotHeight: 800);

        Assert.DoesNotContain("data-offscreen", OverlayDiv(html, "page"));
        Assert.Contains("data-offscreen=\"true\"", OverlayDiv(html, "flyout-row"));
    }

    [Fact]
    public void Render_WithoutScreenshot_DoesNotJudgeOverlaysAgainstThePlaceholderViewport()
    {
        // With no screenshot the inspector falls back to an 800x600 placeholder; a larger window's
        // on-screen controls beyond it must not be marked off-screen.
        var html = HtmlRenderer.Render(
            [Overlay("page", "ContentPage", 0, 0, 1200, 900), Overlay("button", "Button", 900, 700, 100, 40)],
            hasScreenshot: false, screenshotWidth: 800, screenshotHeight: 600);

        Assert.DoesNotContain("data-offscreen", OverlayDiv(html, "button"));
    }

    [Theory]
    [InlineData("Button", true, true, true)]
    [InlineData("CollectionView", true, true, true)]
    [InlineData("Label", true, true, false)]
    [InlineData("Button", true, false, false)]
    [InlineData("Button", false, true, false)]
    public void RenderElements_EmitsInteractabilityFromTraitsAndState(
        string type,
        bool visible,
        bool enabled,
        bool expected)
    {
        var html = HtmlRenderer.RenderElements(new List<ElementInfo>
        {
            new()
            {
                Id = "target",
                Type = type,
                IsVisible = visible,
                IsEnabled = enabled,
                Bounds = new BoundsInfo { X = 0, Y = 0, Width = 20, Height = 20 },
            },
        });

        Assert.Contains($"data-interactable=\"{expected.ToString().ToLowerInvariant()}\"", html);
    }

    [Fact]
    public void ComputeWebSocketAcceptKey_MatchesRfc6455SampleVector()
    {
        var acceptKey = InspectorServer.ComputeWebSocketAcceptKey("dGhlIHNhbXBsZSBub25jZQ==");

        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", acceptKey);
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
