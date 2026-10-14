using DevFlow.Sample.Native.Apple;
using Foundation;
using UIKit;

namespace DevFlow.Sample.Native;

[Register(nameof(SceneDelegate))]
public class SceneDelegate : UIWindowSceneDelegate
{
    public override UIWindow? Window { get; set; }

    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
            throw new InvalidOperationException("The application scene must be a UIWindowScene.");

        Window = new UIWindow(windowScene)
        {
            RootViewController = new UINavigationController(new SampleViewController()),
        };
        Window.MakeKeyAndVisible();
    }

    public override void DidDisconnect(UIScene scene)
    {
        Window = null;
    }
}
