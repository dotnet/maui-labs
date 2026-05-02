using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Tests.Handlers;
using Xunit;

namespace Comet.Tests
{
public class KeyAwareReconciliationTests : TestBase
{
// Component with dynamic keyed children
class KeyedListComponent : Component
{
public List<string> Items { get; set; } = new List<string>();

public override View Render()
{
return new VStack
{
Items.Select(item => new Text(item).Key(item)).ToArray()
};
}
}

[Fact]
public void ViewKeyPropertyCanBeSet()
{
var view = new Text("Hello");
view.Key("my-key");

Assert.Equal("my-key", view.GetKey());
}

[Fact(Skip = "Awaiting Phase 4.1")]
public void ViewKeyPropertyIsChainable()
{
var view = new Text("Hello")
.Key("my-key")
.Color(Microsoft.Maui.Graphics.Colors.Red);

Assert.Equal("my-key", view.GetKey());
Assert.NotNull(view.GetEnvironment<Microsoft.Maui.Graphics.Color>(EnvironmentKeys.Colors.Color));
}

[Fact]
public void KeyedListDiffMatchesByKey()
{
var component = new KeyedListComponent
{
Items = new List<string> { "A", "B", "C" }
};
component.SetViewHandlerToGeneric();

// Change order
component.Items = new List<string> { "C", "A", "B" };
component.Reload();

var builtView = component.BuiltView as VStack;
Assert.NotNull(builtView);
var children = ((IContainerView)builtView).GetChildren().ToList();
Assert.Equal(3, children.Count);

// Verify order matches new Items
Assert.Equal("C", (children[0] as Text)?.Value);
Assert.Equal("A", (children[1] as Text)?.Value);
Assert.Equal("B", (children[2] as Text)?.Value);
}

[Fact]
public void KeyedReorderDetection()
{
// Track instances to verify reuse vs replacement
var component = new KeyedListComponent
{
Items = new List<string> { "A", "B", "C" }
};
component.SetViewHandlerToGeneric();

var firstRender = component.BuiltView as VStack;
var firstChildren = ((IContainerView)firstRender).GetChildren().ToList();
var firstChildA = firstChildren[0];
var firstChildB = firstChildren[1];
var firstChildC = firstChildren[2];

// Reorder: C, A, B
component.Items = new List<string> { "C", "A", "B" };
component.Reload();

var secondRender = component.BuiltView as VStack;
var secondChildren = ((IContainerView)secondRender).GetChildren().ToList();

// With keyed diffing, instances should be reused and moved
Assert.Same(firstChildC, secondChildren[0]); // C moved to front
Assert.Same(firstChildA, secondChildren[1]); // A moved to middle
Assert.Same(firstChildB, secondChildren[2]); // B moved to end
}

[Fact]
public void KeyedAdditionDetection()
{
var component = new KeyedListComponent
{
Items = new List<string> { "A", "B" }
};
component.SetViewHandlerToGeneric();

var firstRender = component.BuiltView as VStack;
var firstChildren = ((IContainerView)firstRender).GetChildren().ToList();
Assert.Equal(2, firstChildren.Count);

// Add a new item
component.Items = new List<string> { "A", "B", "C" };
component.Reload();

var secondRender = component.BuiltView as VStack;
var secondChildren = ((IContainerView)secondRender).GetChildren().ToList();
Assert.Equal(3, secondChildren.Count);

// First two should be reused
Assert.Same(firstChildren[0], secondChildren[0]);
Assert.Same(firstChildren[1], secondChildren[1]);
// Third is new
Assert.NotSame(firstChildren.FirstOrDefault(), secondChildren[2]);
}

[Fact]
public void KeyedRemovalDetection()
{
var component = new KeyedListComponent
{
Items = new List<string> { "A", "B", "C" }
};
component.SetViewHandlerToGeneric();

var firstRender = component.BuiltView as VStack;
var firstChildren = ((IContainerView)firstRender).GetChildren().ToList();
Assert.Equal(3, firstChildren.Count);

// Remove middle item
component.Items = new List<string> { "A", "C" };
component.Reload();

var secondRender = component.BuiltView as VStack;
var secondChildren = ((IContainerView)secondRender).GetChildren().ToList();
Assert.Equal(2, secondChildren.Count);

// First and third should be reused
Assert.Same(firstChildren[0], secondChildren[0]); // A
Assert.Same(firstChildren[2], secondChildren[1]); // C (was third, now second)
}

[Fact]
public void KeyStabilityAcrossReRender()
{
var component = new KeyedListComponent
{
Items = new List<string> { "A", "B", "C" }
};
component.SetViewHandlerToGeneric();

var firstRender = component.BuiltView as VStack;
var firstChildren = ((IContainerView)firstRender).GetChildren().ToList();

// Trigger re-render without changing items
component.Reload();

var secondRender = component.BuiltView as VStack;
var secondChildren = ((IContainerView)secondRender).GetChildren().ToList();

// All children should be reused (same keys, same order)
Assert.Same(firstChildren[0], secondChildren[0]);
Assert.Same(firstChildren[1], secondChildren[1]);
Assert.Same(firstChildren[2], secondChildren[2]);
}

[Fact]
public void EmptyKeyTreatedAsUnkeyed()
{
var stack = new VStack();
var view1 = new Text("First").Key(null);
var view2 = new Text("Second").Key("");
var view3 = new Text("Third"); // no key
stack.Add(view1);
stack.Add(view2);
stack.Add(view3);

stack.SetViewHandlerToGeneric();

// All three should be treated as unkeyed (index-based diffing)
var children = ((IContainerView)stack).GetChildren().ToList();
Assert.Equal(3, children.Count);
}

[Fact]
public void KeyedChildrenWithComplexReorder()
{
var component = new KeyedListComponent
{
Items = new List<string> { "A", "B", "C", "D", "E" }
};
component.SetViewHandlerToGeneric();

var firstChildren = ((IContainerView)(component.BuiltView as VStack)).GetChildren().ToList();

// Complex reorder: reverse order
component.Items = new List<string> { "E", "D", "C", "B", "A" };
component.Reload();

var secondChildren = ((IContainerView)(component.BuiltView as VStack)).GetChildren().ToList();

// All children should be reused, just reordered
Assert.Same(firstChildren[4], secondChildren[0]); // E
Assert.Same(firstChildren[3], secondChildren[1]); // D
Assert.Same(firstChildren[2], secondChildren[2]); // C
Assert.Same(firstChildren[1], secondChildren[3]); // B
Assert.Same(firstChildren[0], secondChildren[4]); // A
}

[Fact]
public void KeyedAddAndRemoveInSameUpdate()
{
var component = new KeyedListComponent
{
Items = new List<string> { "A", "B", "C" }
};
component.SetViewHandlerToGeneric();

var firstChildren = ((IContainerView)(component.BuiltView as VStack)).GetChildren().ToList();

// Remove B, add D
component.Items = new List<string> { "A", "C", "D" };
component.Reload();

var secondChildren = ((IContainerView)(component.BuiltView as VStack)).GetChildren().ToList();
Assert.Equal(3, secondChildren.Count);

// A and C should be reused
Assert.Same(firstChildren[0], secondChildren[0]); // A
Assert.Same(firstChildren[2], secondChildren[1]); // C (moved from index 2 to 1)
// D is new
Assert.Equal("D", (secondChildren[2] as Text)?.Value);
}
}
}
