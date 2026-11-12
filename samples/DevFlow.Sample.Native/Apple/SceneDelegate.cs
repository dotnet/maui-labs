using DevFlow.Sample.Native.Apple;
using Foundation;
using UIKit;

namespace DevFlow.Sample.Native;

[Register(nameof(SceneDelegate))]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
            throw new InvalidOperationException("The application scene must be a UIWindowScene.");

        Window = new UIWindow(windowScene)
        {
            RootViewController = new UINavigationController(new SampleViewController()),
        };
        Window.MakeKeyAndVisible();
    }

    [Export("sceneDidDisconnect:")]
    public void DidDisconnect(UIScene scene)
    {
        Window = null;
    }
}
