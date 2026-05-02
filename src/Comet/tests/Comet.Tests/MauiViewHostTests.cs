using System;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
public class MauiViewHostTests : TestBase
{
[Fact]
public void Constructor_SetsHostedView()
{
var inner = new TestIView();
var host = new MauiViewHost(inner);
Assert.Same(inner, host.HostedView);
}

[Fact]
public void Constructor_NullView_Throws()
{
Assert.Throws<ArgumentNullException>(() => new MauiViewHost((IView)null));
}

[Fact]
public void Factory_DefersCreation()
{
bool created = false;
var host = new MauiViewHost(() => { created = true; return new TestIView(); });
Assert.False(created);
_ = host.HostedView;
Assert.True(created);
}

[Fact]
public void Factory_NullThrows()
{
Assert.Throws<ArgumentNullException>(() => new MauiViewHost((Func<IView>)null));
}

[Fact]
public void Factory_CalledOnce()
{
int count = 0;
var host = new MauiViewHost(() => { count++; return new TestIView(); });
_ = host.HostedView;
_ = host.HostedView;
Assert.Equal(1, count);
}

[Fact]
public void GetDesiredSize_RespectsFullFrameConstraints()
{
var host = new MauiViewHost(new TestIView());
host.Frame(width: 200, height: 100);
var size = host.GetDesiredSize(new Size(500, 500));
Assert.Equal(200, size.Width);
Assert.Equal(100, size.Height);
}

[Fact]
public void GetDesiredSize_RespectsPartialHeight()
{
var host = new MauiViewHost(new TestIView());
host.Frame(height: 50);
var size = host.GetDesiredSize(new Size(300, 500));
Assert.Equal(50, size.Height);
Assert.True(size.Width > 0);
}

[Fact]
		public void CanBeChildOfVStack()
		{
			var host = new MauiViewHost(new TestIView()).Frame(height: 40);
			var stack = new VStack { host };
			Assert.True(((System.Collections.Generic.IEnumerable<View>)stack).Any(v => v == host));
			Assert.Equal(stack, host.Parent);
		}

[Fact]
public void Dispose_CleansUp()
{
var d = new DisposableView();
var host = new MauiViewHost(d);
_ = host.HostedView;
host.Dispose();
Assert.True(d.IsDisposed);
Assert.Null(host.HostedView);
}

[Fact]
public void ReplacedView_ReturnsSelf()
{
var host = new MauiViewHost(new TestIView());
var replaceable = (IReplaceableView)host;
Assert.Same(host, replaceable.ReplacedView);
}

class TestIView : IView
{
public string AutomationId => "";
public FlowDirection FlowDirection => FlowDirection.LeftToRight;
Microsoft.Maui.Primitives.LayoutAlignment IView.HorizontalLayoutAlignment => Microsoft.Maui.Primitives.LayoutAlignment.Fill;
Microsoft.Maui.Primitives.LayoutAlignment IView.VerticalLayoutAlignment => Microsoft.Maui.Primitives.LayoutAlignment.Fill;
public Semantics Semantics => null;
public IShape Clip => null;
public IShadow Shadow => null;
public bool IsEnabled => true;
public bool IsFocused { get; set; }
public Visibility Visibility => Visibility.Visible;
public double Opacity => 1;
public Paint Background => null;
public Rect Frame { get; set; }
public double Width => -1;
public double MinimumWidth => -1;
public double MaximumWidth => -1;
public double Height => -1;
public double MinimumHeight => -1;
public double MaximumHeight => -1;
public Thickness Margin => Thickness.Zero;
public Size DesiredSize => new Size(100, 40);
public int ZIndex => 0;
public bool InputTransparent => false;
public double TranslationX => 0;
public double TranslationY => 0;
public double Scale => 1;
public double ScaleX => 1;
public double ScaleY => 1;
public double Rotation => 0;
public double RotationX => 0;
public double RotationY => 0;
public double AnchorX => 0.5;
public double AnchorY => 0.5;
public IViewHandler Handler { get; set; }
IElementHandler IElement.Handler { get; set; }
public IElement Parent => null;
public Size Arrange(Rect bounds) => bounds.Size;
public bool Focus() => false;
public void Unfocus() { }
public void InvalidateArrange() { }
public void InvalidateMeasure() { }
public Size Measure(double w, double h) => new Size(100, 40);
}

class DisposableView : TestIView, IDisposable
{
public bool IsDisposed { get; private set; }
public void Dispose() => IsDisposed = true;
}
}
}
