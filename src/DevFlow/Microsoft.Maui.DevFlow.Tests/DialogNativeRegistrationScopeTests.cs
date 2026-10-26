using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Tests for <see cref="DialogNativeRegistrationScope"/>, the lifecycle helper AppKit's
/// <c>AlertManagerSubscription</c> uses to guarantee deterministic unregistration of the
/// native elements (dialog surface, buttons, prompt input) it registers with DevFlow while a
/// dialog is presented. The helper has no AppKit dependency, so it is compiled directly into
/// this test project (see the Compile Include in the .csproj) and exercised without needing
/// an AppKit/macOS runtime.
/// </summary>
public class DialogNativeRegistrationScopeTests
{
    [Fact]
    public void Dispose_UnregistersAllTrackedElementsInReverseOrder()
    {
        var unregistered = new List<object>();
        var scope = new DialogNativeRegistrationScope(unregistered.Add);
        var dialogSurface = new object();
        var button1 = new object();
        var button2 = new object();

        scope.Track(dialogSurface);
        scope.Track(button1);
        scope.Track(button2);
        scope.Dispose();

        Assert.Equal(new[] { button2, button1, dialogSurface }, unregistered);
    }

    [Fact]
    public void Dispose_WithNoTrackedElements_UnregistersNothing()
    {
        var unregistered = new List<object>();
        var scope = new DialogNativeRegistrationScope(unregistered.Add);

        scope.Dispose();

        Assert.Empty(unregistered);
    }

    [Fact]
    public void Dispose_CalledTwice_OnlyUnregistersOnce()
    {
        var unregisterCallCount = 0;
        var scope = new DialogNativeRegistrationScope(_ => unregisterCallCount++);
        scope.Track(new object());

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(1, unregisterCallCount);
    }

    [Fact]
    public void Dispose_AfterPartialRegistrationFollowedByException_StillUnregistersTrackedElements()
    {
        var unregistered = new List<object>();
        var scope = new DialogNativeRegistrationScope(unregistered.Add);
        var dialogSurface = new object();
        var button = new object();

        try
        {
            scope.Track(dialogSurface);
            scope.Track(button);
            throw new InvalidOperationException("Simulated failure while registering the prompt input");
        }
        catch (InvalidOperationException)
        {
            // Expected: mirrors a try/finally around presentation where an exception is
            // thrown after some, but not all, elements were registered.
        }
        finally
        {
            scope.Dispose();
        }

        Assert.Equal(new[] { button, dialogSurface }, unregistered);
    }

    [Fact]
    public void Track_AfterDispose_Throws()
    {
        var scope = new DialogNativeRegistrationScope(_ => { });
        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(() => scope.Track(new object()));
    }

    [Fact]
    public void Track_NullElement_Throws()
    {
        var scope = new DialogNativeRegistrationScope(_ => { });

        Assert.Throws<ArgumentNullException>(() => scope.Track(null!));
    }

    [Fact]
    public void Constructor_NullUnregisterDelegate_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DialogNativeRegistrationScope(null!));
    }

    [Fact]
    public void TrackedCount_ReflectsTrackedElements_AndResetsAfterDispose()
    {
        var scope = new DialogNativeRegistrationScope(_ => { });
        scope.Track(new object());
        scope.Track(new object());

        Assert.Equal(2, scope.TrackedCount);

        scope.Dispose();

        Assert.Equal(0, scope.TrackedCount);
    }
}
