using System.Net;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Shiny.BluetoothLE;
using ShinyBle.DevFlowPlugin;
using Xunit;

namespace ShinyBle.DevFlowPlugin.Tests;

/// <summary>
/// Runs the Shiny BLE plugin inside a real DevFlow agent, with a fake Shiny manager behind the monitor,
/// and reads it back the way the CLI and MCP do: through <see cref="AgentClient"/>.
/// </summary>
public class ShinyBleDevFlowExtensionTests
{
    [Fact]
    public async Task AppActivity_IsVisibleThroughTheAgent()
    {
        await using var app = await TestApp.StartAsync();

        // What an app does: scan, connect, read, subscribe, write.
        var results = new List<ScanResult>();
        using (app.Ble.Scan().Subscribe(results.Add))
        {
            app.Fake.Advertise(app.Heart, rssi: -60);
            app.Fake.Advertise(app.Heart, rssi: -61); // throttled: same device, same signal
        }
        var heart = Assert.IsType<MonitoredPeripheral>(results[0].Peripheral);
        heart.Connect(null);
        app.Heart.SetStatus(ConnectionState.Connected);
        await heart.ReadCharacteristic("180d", "2a38");
        using (heart.NotifyCharacteristic("180d", "2a37").Subscribe())
            app.Heart.Notify("2a37", [0x06, 0x48]);
        await heart.WriteCharacteristic("180d", "2a39", [0x01]);

        var extension = (await app.Client.GetExtensionsAsync())[ShinyBleDevFlowExtension.ExtensionNamespace];
        Assert.Equal("ready", extension.State);
        Assert.Equal("ble", extension.ToolPrefix);
        Assert.Equal(["clear", "events", "peripherals", "status"], extension.Tools.Select(t => t.Name).Order());

        var page = await app.Client.GetExtensionEventsAsync(ShinyBleDevFlowExtension.ExtensionNamespace, "events");
        Assert.Equal(
            ["scanStart", "scan", "scanStop", "connect", "status", "read", "notify", "write"],
            page.Events.Select(e => e.Data.GetProperty("kind").GetString()));
        var notify = page.Events.Single(e => e.Data.GetProperty("kind").GetString() == "notify").Data;
        Assert.Equal("0648", notify.GetProperty("value").GetString());
        Assert.Equal("Heart Monitor", notify.GetProperty("name").GetString());
        Assert.Equal("Connected", page.Events.Single(e => e.Data.GetProperty("kind").GetString() == "status").Data.GetProperty("detail").GetString());

        var status = await CallAsync(app.Client, extension, "status");
        Assert.True(status.GetProperty("monitored").GetBoolean());
        Assert.Equal(1, status.GetProperty("knownPeripherals").GetInt32());

        var peripherals = await CallAsync(app.Client, extension, "peripherals", """{"connectedOnly":true}""");
        var listed = Assert.Single(peripherals.EnumerateArray());
        Assert.Equal("Heart Monitor", listed.GetProperty("name").GetString());
        Assert.Equal(-61, listed.GetProperty("rssi").GetInt32()); // the latest signal, though its event was throttled
        Assert.Equal(1, listed.GetProperty("reads").GetInt32());
        Assert.Equal(1, listed.GetProperty("writes").GetInt32());
        Assert.Equal(1, listed.GetProperty("notifications").GetInt32());
    }

    [Fact]
    public async Task FailedOperations_AreRecorded_AndStillReachTheApp()
    {
        await using var app = await TestApp.StartAsync();
        var heart = app.Ble.GetKnownPeripheral(app.Heart.Uuid)!;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await heart.ReadCharacteristic("180d", "dead"));

        var page = await app.Client.GetExtensionEventsAsync(ShinyBleDevFlowExtension.ExtensionNamespace, "events");
        var error = Assert.Single(page.Events).Data;
        Assert.Equal("error", error.GetProperty("kind").GetString());
        Assert.Equal("dead", error.GetProperty("characteristic").GetString());
        Assert.Contains("read: no such characteristic", error.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Clear_EmptiesThePluginBuffers()
    {
        await using var app = await TestApp.StartAsync(requireLease: false);
        using (app.Ble.Scan().Subscribe())
            app.Fake.Advertise(app.Heart, rssi: -70);

        var extension = (await app.Client.GetExtensionsAsync())[ShinyBleDevFlowExtension.ExtensionNamespace];
        await CallAsync(app.Client, extension, "clear", "{}");

        var page = await app.Client.GetExtensionEventsAsync(ShinyBleDevFlowExtension.ExtensionNamespace, "events");
        Assert.Empty(page.Events);
        Assert.Empty((await CallAsync(app.Client, extension, "peripherals")).EnumerateArray());
    }

    [Fact]
    public void AddShinyBleDevFlowMonitor_RequiresAddBluetoothLEFirst()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddShinyBleDevFlowMonitor());
        Assert.Contains("AddBluetoothLE", ex.Message);
    }

    static async Task<JsonElement> CallAsync(AgentClient client, ExtensionDescriptor extension, string tool, string? parameters = null)
    {
        var descriptor = extension.Tools.Single(t => t.Name == tool);
        JsonElement? json = parameters is null ? null : JsonDocument.Parse(parameters).RootElement.Clone();
        using var doc = JsonDocument.Parse(await client.CallExtensionToolAsync(descriptor.Method, descriptor.Path, json));
        return doc.RootElement.Clone();
    }

    sealed class TestApp : IAsyncDisposable
    {
        ServiceProvider _services = null!;
        DevFlowAgentService _agent = null!;

        public FakeBleManager Fake { get; } = new();
        public FakePeripheral Heart { get; } = new("11111111-2222-3333-4444-555555555555", "Heart Monitor");
        public IBleManager Ble { get; private set; } = null!;
        public AgentClient Client { get; private set; } = null!;

        public static async Task<TestApp> StartAsync(bool requireLease = true)
        {
            var app = new TestApp();
            app.Fake.Known.Add(app.Heart);

            // The same registration an app makes, with a fake standing in for AddBluetoothLE().
            var services = new ServiceCollection();
            services.AddSingleton<IBleManager>(app.Fake);
            services.AddShinyBleDevFlowMonitor();
            app._services = services.BuildServiceProvider();
            app.Ble = app._services.GetRequiredService<IBleManager>();

            var port = GetFreePort();
            var options = new AgentOptions { Port = port, Services = app._services, RequireMutationLease = requireLease };
            options.AddExtension<ShinyBleDevFlowExtension>();
            app._agent = new DevFlowAgentService(options);
            app._agent.StartServerOnly(null);
            app.Client = new AgentClient("localhost", port);

            await Task.CompletedTask;
            return app;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _agent.StopAsync();
            _agent.Dispose();
            await _services.DisposeAsync();
        }

        static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    sealed class FakeBleManager : IBleManager
    {
        readonly Subject<ScanResult> _scan = new();

        public List<FakePeripheral> Known { get; } = [];
        public AccessState CurrentAccess => AccessState.Available;
        public bool IsScanning { get; private set; }

        public IObservable<AccessState> RequestAccess() => Observable.Return(AccessState.Available);
        public IPeripheral? GetKnownPeripheral(string peripheralUuid) => Known.FirstOrDefault(p => p.Uuid == peripheralUuid);
        public IEnumerable<IPeripheral> GetConnectedPeripherals() => Known.Where(p => p.Status == ConnectionState.Connected);
        public void StopScan() => IsScanning = false;

        public IObservable<ScanResult> Scan(ScanConfig? scanConfig = null) => Observable.Create<ScanResult>(observer =>
        {
            IsScanning = true;
            var sub = _scan.Subscribe(observer);
            return () =>
            {
                IsScanning = false;
                sub.Dispose();
            };
        });

        public void Advertise(FakePeripheral peripheral, int rssi)
            => _scan.OnNext(new ScanResult(peripheral, rssi, new FakeAdvertisement(peripheral.Name)));
    }

    sealed class FakeAdvertisement(string? localName) : IAdvertisementData
    {
        public string? LocalName => localName;
        public bool? IsConnectable => true;
        public AdvertisementServiceData[]? ServiceData => null;
        public ManufacturerData? ManufacturerData => null;
        public string[]? ServiceUuids => ["180d"];
        public int? TxPower => null;
    }

    sealed class FakePeripheral(string uuid, string name) : IPeripheral
    {
        readonly BehaviorSubject<ConnectionState> _status = new(ConnectionState.Disconnected);
        readonly Subject<BleCharacteristicResult> _notifications = new();

        public string Uuid => uuid;
        public string? Name => name;
        public int Mtu => 185;
        public ConnectionState Status => _status.Value;

        public void SetStatus(ConnectionState state) => _status.OnNext(state);

        public void Notify(string characteristic, byte[] data)
            => _notifications.OnNext(new BleCharacteristicResult(Info(characteristic), BleCharacteristicEvent.Notification, data));

        public void Connect(ConnectionConfig? config) { }
        public void CancelConnection() => SetStatus(ConnectionState.Disconnected);
        public IObservable<ConnectionState> WhenStatusChanged() => _status;
        public IObservable<BleException> WhenConnectionFailed() => Observable.Never<BleException>();
        public IObservable<Unit> WhenServicesChanged() => Observable.Never<Unit>();
        public IObservable<int> ReadRssi() => Observable.Return(-50);
        public IObservable<BleServiceInfo> GetService(string serviceUuid) => Observable.Return(new BleServiceInfo(serviceUuid));
        public IObservable<IReadOnlyList<BleServiceInfo>> GetServices() => Observable.Return<IReadOnlyList<BleServiceInfo>>([new BleServiceInfo("180d")]);
        public IObservable<BleCharacteristicInfo> GetCharacteristic(string serviceUuid, string characteristicUuid) => Observable.Return(Info(characteristicUuid));
        public IObservable<IReadOnlyList<BleCharacteristicInfo>> GetCharacteristics(string serviceUuid) => Observable.Return<IReadOnlyList<BleCharacteristicInfo>>([]);
        public IObservable<BleCharacteristicResult> NotifyCharacteristic(string serviceUuid, string characteristicUuid, bool useIndicationsIfAvailable = true)
            => _notifications.Where(r => r.Characteristic.Uuid == characteristicUuid);
        public IObservable<BleCharacteristicInfo> WhenCharacteristicSubscriptionChanged(string serviceUuid, string characteristicUuid) => Observable.Never<BleCharacteristicInfo>();

        public IObservable<BleCharacteristicResult> ReadCharacteristic(string serviceUuid, string characteristicUuid)
            => characteristicUuid == "dead"
                ? Observable.Throw<BleCharacteristicResult>(new InvalidOperationException("no such characteristic"))
                : Observable.Return(new BleCharacteristicResult(Info(characteristicUuid), BleCharacteristicEvent.Read, [0x01]));

        public IObservable<BleCharacteristicResult> WriteCharacteristic(string serviceUuid, string characteristicUuid, byte[] data, bool withResponse = true)
            => Observable.Return(new BleCharacteristicResult(Info(characteristicUuid), BleCharacteristicEvent.Write, data));

        public IObservable<BleDescriptorInfo> GetDescriptor(string serviceUuid, string characteristicUuid, string descriptorUuid) => Observable.Never<BleDescriptorInfo>();
        public IObservable<IReadOnlyList<BleDescriptorInfo>> GetDescriptors(string serviceUuid, string characteristicUuid) => Observable.Never<IReadOnlyList<BleDescriptorInfo>>();
        public IObservable<BleDescriptorResult> ReadDescriptor(string serviceUuid, string characteristicUuid, string descriptorUuid) => Observable.Never<BleDescriptorResult>();
        public IObservable<BleDescriptorResult> WriteDescriptor(string serviceUuid, string characteristicUuid, string descriptorUuid, byte[] data) => Observable.Never<BleDescriptorResult>();

        static BleCharacteristicInfo Info(string uuid)
            => new(new BleServiceInfo("180d"), uuid, false, CharacteristicProperties.Read | CharacteristicProperties.Notify | CharacteristicProperties.Write);
    }
}
