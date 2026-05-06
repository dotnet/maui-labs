using System;
using System.Collections.Generic;
using Android.Content;
using Android.Views;
using Android.Widget;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using AndroidX.Activity;
using AView = Android.Views.View;

namespace Comet.Android.Controls
{
public class CometNavigationView : CustomFrameLayout
{
IMauiContext MauiContext { get; set; }
readonly Stack<View> viewStack = new();
AView currentPlatformView;
IView currentVirtualView;
View currentView;

public CometNavigationView(IMauiContext context) : base(context.Context)
{
MauiContext = context;
}

public void SetRoot(View view)
{
if (!isAttached)
{
contentView = view;
}
else
{
viewStack.Clear();
ShowView(view);
}
}

void ShowView(View view)
{
if (currentPlatformView is not null)
{
RemoveView(currentPlatformView);
currentPlatformView = null;
currentVirtualView = null;
}

currentView = view;

var renderView = view.GetView();
IView viewToRender = (renderView is not null && renderView != view) ? renderView : view;

var platformView = viewToRender.ToPlatform(MauiContext);
if (platformView is not null)
{
if (platformView.Parent is not null)
(platformView.Parent as ViewGroup)?.RemoveView(platformView);
currentPlatformView = platformView;
currentVirtualView = viewToRender;
AddView(currentPlatformView, new FrameLayout.LayoutParams(
LayoutParams.MatchParent, LayoutParams.MatchParent));
ForceLayout();
}
}

void ForceLayout()
{
RequestLayout();
// Post a deferred layout to ensure the view tree is fully measured
Post(() =>
{
if (currentPlatformView is not null && Width > 0 && Height > 0)
{
MeasureAndArrange();
currentPlatformView.Layout(0, 0, Width, Height);
// Force a draw pass - without this, complex views added
// dynamically during navigation never get their initial draw
InvalidateViewTree(currentPlatformView);
}
});
}

static void InvalidateViewTree(AView view)
{
view.Invalidate();
if (view is ViewGroup vg)
{
for (int i = 0; i < vg.ChildCount; i++)
InvalidateViewTree(vg.GetChildAt(i));
}
}

void MeasureAndArrange()
{
if (currentVirtualView is null || Width <= 0 || Height <= 0)
return;
var density = Context?.Resources?.DisplayMetrics?.Density ?? 1;
var widthDp = Width / density;
var heightDp = Height / density;
currentVirtualView.Measure(widthDp, heightDp);
currentVirtualView.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, widthDp, heightDp));
}

public void NavigateTo(View view)
{
if (currentView is not null)
viewStack.Push(currentView);
ShowView(view);
UpdateBackCallbackState();
}

public bool CanGoBack => viewStack.Count > 0;

OnBackPressedCallback backCallback;

bool isAttached = false;
View contentView;
protected override void OnAttachedToWindow()
{
base.OnAttachedToWindow();
isAttached = true;
if (contentView is not null)
{
SetRoot(contentView);
contentView = null;
}
RegisterBackHandler();
}

protected override void OnDetachedFromWindow()
{
base.OnDetachedFromWindow();
backCallback?.Remove();
backCallback = null;
isAttached = false;
}

void RegisterBackHandler()
{
if (Context is ComponentActivity activity)
{
backCallback = new NavigationBackCallback(this);
activity.OnBackPressedDispatcher.AddCallback(backCallback);
UpdateBackCallbackState();
}
}

void UpdateBackCallbackState()
{
if (backCallback is not null)
backCallback.Enabled = CanGoBack;
}

class NavigationBackCallback : OnBackPressedCallback
{
readonly CometNavigationView nav;
public NavigationBackCallback(CometNavigationView nav) : base(false)
{
this.nav = nav;
}
public override void HandleOnBackPressed()
{
nav.Pop();
}
}

protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
{
base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
var widthSize = AView.MeasureSpec.GetSize(widthMeasureSpec);
var heightSize = AView.MeasureSpec.GetSize(heightMeasureSpec);
for (int i = 0; i < ChildCount; i++)
{
var child = GetChildAt(i);
child?.Measure(
AView.MeasureSpec.MakeMeasureSpec(widthSize, global::Android.Views.MeasureSpecMode.Exactly),
AView.MeasureSpec.MakeMeasureSpec(heightSize, global::Android.Views.MeasureSpecMode.Exactly));
}
}

protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
{
var width = right - left;
var height = bottom - top;

MeasureAndArrange();

for (int i = 0; i < ChildCount; i++)
{
var child = GetChildAt(i);
child?.Layout(0, 0, width, height);
}
}

public void Pop()
{
if (viewStack.Count > 0)
{
var previous = viewStack.Pop();
ShowView(previous);
UpdateBackCallbackState();
}
}
}
}
