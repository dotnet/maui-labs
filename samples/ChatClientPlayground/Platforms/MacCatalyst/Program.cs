using UIKit;

namespace ChatClientPlayground;

/// <summary>Mac Catalyst application entry point.</summary>
public class Program
{
    /// <summary>Starts the Mac Catalyst application.</summary>
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
