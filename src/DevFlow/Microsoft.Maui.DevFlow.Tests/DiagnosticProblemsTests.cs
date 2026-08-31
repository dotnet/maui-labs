using System.Net;
using System.Net.Sockets;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Diagnostics;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;
using CoreDiagnosticProblem = Microsoft.Maui.DevFlow.Agent.Core.Diagnostics.DiagnosticProblem;
using DriverDiagnosticProblemBatch = Microsoft.Maui.DevFlow.Driver.DiagnosticProblemBatch;

namespace Microsoft.Maui.DevFlow.Tests;

public class DiagnosticProblemsTests
{
    [Fact]
    public void ProblemStore_DeduplicatesAndEvictsOldest()
    {
        var store = new DiagnosticProblemStore(2);
        var now = DateTime.UtcNow;

        store.Add(Problem("one", now));
        store.Add(Problem("one", now.AddSeconds(1)));
        store.Add(Problem("two", now.AddSeconds(2)));
        store.Add(Problem("three", now.AddSeconds(3)));

        var snapshot = store.Snapshot(enabled: true, limit: 10);

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(1, snapshot.Evicted);
        Assert.DoesNotContain(snapshot.Problems, problem => problem.Id == "one");
        Assert.Contains(snapshot.Problems, problem => problem.Id == "two");
        Assert.Contains(snapshot.Problems, problem => problem.Id == "three");
    }

    [Fact]
    public void ProblemStore_ClearAdvancesTheMonotonicRevision()
    {
        var store = new DiagnosticProblemStore(2);
        var added = store.Add(Problem("one", DateTime.UtcNow));

        store.Clear();
        var cleared = store.Snapshot(enabled: true, limit: 10);

        Assert.Equal(added.Revision + 1, cleared.Revision);
        Assert.Equal(0, cleared.Count);
        Assert.Empty(cleared.Problems);
    }

    [Fact]
    public async Task BindingFailure_IsCapturedWithoutRetainingSourceValues()
    {
        var label = new Label
        {
            AutomationId = "binding-problem-label",
            BindingContext = new BindingSource()
        };

        using var harness = await ProblemHarness.CreateAsync(label);
        await harness.Client.GetTreeAsync();

        label.SetBinding(Label.TextProperty, "MissingProperty");

        DriverDiagnosticProblemBatch? batch = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            batch = await harness.Client.GetDiagnosticProblemsAsync();
            if (batch.Problems.Count > 0)
                break;
            await Task.Delay(50);
        }

        var problem = Assert.Single(batch!.Problems);
        Assert.Equal("binding", problem.Kind);
        Assert.Equal(nameof(Label.Text), problem.Property);
        Assert.Equal("MissingProperty", problem.BindingPath);
        Assert.Equal(label.AutomationId, problem.ElementId);
        Assert.DoesNotContain("secret-value", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindingConversionFailure_DoesNotRetainRejectedValue()
    {
        const string secret = "CorrectHorseBatteryStaple!";
        var slider = new Slider
        {
            AutomationId = "binding-conversion-slider",
            BindingContext = new ConversionBindingSource { SecretNumber = secret }
        };

        using var harness = await ProblemHarness.CreateAsync(slider);
        await harness.Client.GetTreeAsync();
        slider.SetBinding(Slider.ValueProperty, nameof(ConversionBindingSource.SecretNumber));

        DriverDiagnosticProblemBatch? batch = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            batch = await harness.Client.GetDiagnosticProblemsAsync();
            if (batch.Problems.Count > 0)
                break;
            await Task.Delay(50);
        }

        var problem = Assert.Single(batch!.Problems);
        Assert.Equal(nameof(ConversionBindingSource.SecretNumber), problem.BindingPath);
        Assert.DoesNotContain(secret, problem.Message, StringComparison.Ordinal);
        Assert.Contains("MAUI binding failure", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticRedactor_RedactsAndBoundsMessages()
    {
        var redacted = DiagnosticRedactor.RedactText(
            "Authorization=Bearer abcdefghijklmnopqrstuvwxyz token=super-secret "
            + new string('x', 3000));

        Assert.Contains("<redacted>", redacted);
        Assert.DoesNotContain("super-secret", redacted);
        Assert.True(redacted.Length <= 2049);
    }

    [Fact]
    public void BindingSourceFile_RelativeUri_DoesNotUseLocalPath()
    {
        var source = DevFlowAgentService.GetBindingSourceFile(
            new Uri("Pages/MainPage.xaml", UriKind.Relative));

        Assert.Equal("Pages/MainPage.xaml", source);
    }

    private static CoreDiagnosticProblem Problem(string id, DateTime timestamp)
        => new()
        {
            Id = id,
            Kind = "binding",
            Message = id,
            FirstSeenUtc = timestamp,
            LastSeenUtc = timestamp
        };

    private sealed class BindingSource
    {
        public string ExistingProperty { get; set; } = "secret-value";
    }

    private sealed class ConversionBindingSource
    {
        public string SecretNumber { get; set; } = "";
    }

    private sealed class ProblemHarness : IDisposable
    {
        private readonly DevFlowAgentService _service;

        private ProblemHarness(DevFlowAgentService service, AgentClient client)
        {
            _service = service;
            Client = client;
        }

        public AgentClient Client { get; }

        public static async Task<ProblemHarness> CreateAsync(params View[] views)
        {
            var service = new DevFlowAgentService(new AgentOptions
            {
                Port = GetFreePort(),
                EnableBindingProblems = true,
                EnableMauiDiagnostics = true
            });
            var client = new AgentClient("localhost", service.Port);
            service.StartServerOnly(new ImmediateDispatcher());
            service.BindApp(new TestApplication(views));

            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (await client.GetStatusAsync() is not null)
                    return new ProblemHarness(service, client);
                await Task.Delay(50);
            }

            client.Dispose();
            service.Dispose();
            throw new InvalidOperationException("DevFlow problem test agent did not start.");
        }

        public void Dispose()
        {
            Client.Dispose();
            _service.Dispose();
        }

        private static int GetFreePort() => TestPorts.Reserve();
    }

    private sealed class TestApplication : Application, IVisualTreeElement
    {
        private readonly IReadOnlyList<IVisualTreeElement> _children;

        public TestApplication(IEnumerable<View> views)
        {
            _children = views.Cast<IVisualTreeElement>().ToArray();
        }

        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => _children;

        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => null;
    }

    private sealed class ImmediateDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
    }

    private sealed class ImmediateDispatcherTimer : IDispatcherTimer
    {
        public bool IsRepeating { get; set; }
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }
}
