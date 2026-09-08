using Android.Util;
using AndroidX.Activity;
using AndroidX.Compose.Runtime;
using AndroidX.Compose.Runtime.Internal;
using AndroidX.Compose.UI.Platform;

namespace AndroidX.Compose;

public static partial class ComposeExtensions
{
    const string TAG = "AndroidX.Compose";

    /// <summary>
    /// Sets the activity's content view to a Compose composition that runs
    /// <paramref name="content"/> on each pass and renders the returned
    /// tree. C# parity of Kotlin's
    /// <c>ComponentActivity.setContent { ... }</c> extension (which the
    /// binder strips because its body is a <c>@Composable</c> lambda).
    ///
    /// <code>
    /// protected override void OnCreate(Bundle? state)
    /// {
    ///     base.OnCreate(state);
    ///     this.EnableEdgeToEdge();
    ///     this.SetContent(c =&gt;
    ///     {
    ///         var count = c.MutableStateOf(0);
    ///         return new MaterialTheme { new Text($"Count: {count}") };
    ///     });
    /// }
    /// </code>
    /// </summary>
    public static void SetContent(
        this ComponentActivity activity,
        Func<IComposer, ComposableNode> content)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(content);
        var view = new ComposeView(activity);
        view.SetContent(content);
        activity.SetContentView(view);
        Log.Debug(TAG, "ComponentActivity content set");
    }

    /// <summary>
    /// Installs <paramref name="content"/> as the composition driving this
    /// <see cref="ComposeView"/> — the View-hierarchy entry point. Use when
    /// you're hosting Compose inside an existing Android <c>View</c> tree
    /// (XML layouts, fragments, <c>RecyclerView</c> cells) instead of via
    /// <see cref="SetContent(ComponentActivity, Func{IComposer, ComposableNode})"/>.
    /// Mirrors Kotlin's <c>ComposeView.setContent { ... }</c> extension.
    /// </summary>
    public static void SetContent(
        this ComposeView view,
        Func<IComposer, ComposableNode> content)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(content);
        view.SetContent(ComposableLambdaKt.ComposableLambdaInstance(
            key:     -1,
            tracked: false,
            block:   new ComposableLambda2(composer => content(composer).Render(composer))));
    }

    /// <summary>
    /// Opts the window into edge-to-edge — transparent system bars,
    /// content draws under the status / nav bar, inset padding becomes the
    /// caller's responsibility. Mirrors Kotlin's
    /// <c>ComponentActivity.enableEdgeToEdge()</c>. Call after
    /// <c>base.OnCreate</c> and before
    /// <see cref="SetContent(ComponentActivity, Func{IComposer, ComposableNode})"/>.
    /// </summary>
    public static void EnableEdgeToEdge(this ComponentActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        EdgeToEdge.Enable(activity);
    }
}
