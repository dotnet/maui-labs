using System.Text.Json;
using AppKit;

namespace MacOS.RuntimeTests;

static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args is ["--list"])
            {
                var scenarios = ScenarioRegistry.All.Select(s => new
                {
                    s.Name, s.ExpectedCases, s.ExpectedAssertions, s.TimeoutSeconds,
                    Managed = s.RunManaged != null
                }).ToArray();
                if (scenarios.Length == 0)
                    throw new InvalidOperationException("No scenarios registered.");
                Console.WriteLine(JsonSerializer.Serialize(scenarios));
                return 0;
            }

            if (args is not ["--scenario", var name, "--evidence", var directory])
                throw new ArgumentException("Usage: --list | --scenario <registered-id> --evidence <directory>");

            var scenario = ScenarioRegistry.Get(name);
            var context = new RuntimeTestContext(scenario, directory);
            using var timeout = new System.Threading.Timer(_ =>
                context.Fail(new TimeoutException($"Scenario {name} exceeded {scenario.TimeoutSeconds} seconds.")),
                null, TimeSpan.FromSeconds(scenario.TimeoutSeconds), Timeout.InfiniteTimeSpan);
            try
            {
                if (scenario.RunManaged is { } managed)
                {
                    managed(context);
                    context.Complete();
                }
                else
                {
                    NSApplication.Init();
                    NSApplication.SharedApplication.Appearance = NSAppearance.GetAppearance(NSAppearance.NameAqua);
                    using var appDelegate = scenario.CreateDelegate!(context);
                    NSApplication.SharedApplication.Delegate = appDelegate;
                    NSApplication.Main(Array.Empty<string>());
                    GC.KeepAlive(appDelegate);
                    throw new InvalidOperationException("Native run loop returned without completing the scenario.");
                }
            }
            catch (Exception ex)
            {
                context.Fail(ex);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL runtime host: {ex}");
        }
        return 1;
    }
}
