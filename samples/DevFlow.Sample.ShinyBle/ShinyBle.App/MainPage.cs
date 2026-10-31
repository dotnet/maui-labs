using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Shiny;
using Shiny.BluetoothLE;

namespace ShinyBle.App;

/// <summary>
/// Scan, connect, read and subscribe: enough Bluetooth traffic for the DevFlow plugin to show.
/// Follow it with <c>maui devflow extensions watch org.shiny.ble events</c>.
/// </summary>
public class MainPage : ContentPage
{
    readonly IBleManager _ble;
    readonly ObservableCollection<DeviceRow> _devices = [];
    readonly Label _status = new() { AutomationId = "Status", Text = "Idle" };
    readonly Button _scan = new() { AutomationId = "ScanButton", Text = "Scan" };
    readonly Button _disconnect = new() { AutomationId = "DisconnectButton", Text = "Disconnect", IsEnabled = false };
    IDisposable? _scanSub;
    IPeripheral? _connected;
    readonly List<IDisposable> _notifySubs = [];

    public MainPage(IBleManager ble)
    {
        _ble = ble;
        Title = "Shiny BLE + DevFlow";

        _scan.Clicked += async (_, _) => await ToggleScanAsync();
        _disconnect.Clicked += (_, _) => Disconnect();

        var list = new CollectionView
        {
            AutomationId = "DeviceList",
            ItemsSource = _devices,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() =>
            {
                var name = new Label { FontAttributes = FontAttributes.Bold };
                name.SetBinding(Label.TextProperty, nameof(DeviceRow.Name));
                name.SetBinding(AutomationIdProperty, nameof(DeviceRow.AutomationId));
                var detail = new Label { FontSize = 12, TextColor = Colors.Gray };
                detail.SetBinding(Label.TextProperty, nameof(DeviceRow.Detail));
                return new VerticalStackLayout { Padding = new Thickness(12, 8), Children = { name, detail } };
            })
        };
        list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is DeviceRow row)
            {
                list.SelectedItem = null;
                await ConnectAsync(row.Peripheral);
            }
        };

        Content = new Grid
        {
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)],
            Padding = 12,
            RowSpacing = 8,
            Children =
            {
                new HorizontalStackLayout { Spacing = 8, Children = { _scan, _disconnect } },
                _status.Row(1),
                list.Row(2)
            }
        };
    }

    async Task ToggleScanAsync()
    {
        if (_scanSub is not null)
        {
            StopScan();
            return;
        }

        var access = await _ble.RequestAccess();
        if (access != AccessState.Available)
        {
            _status.Text = $"Bluetooth access: {access}";
            return;
        }

        _devices.Clear();
        _scan.Text = "Stop";
        _status.Text = "Scanning...";
        _scanSub = _ble
            .Scan()
            .ObserveOn(SynchronizationContext.Current!)
            .Subscribe(
                result =>
                {
                    var row = _devices.FirstOrDefault(d => d.Peripheral.Uuid == result.Peripheral.Uuid);
                    if (row is null)
                        _devices.Add(new DeviceRow(result.Peripheral, result.AdvertisementData?.LocalName, result.Rssi));
                    else
                        row.Update(result.Rssi);
                },
                ex =>
                {
                    _status.Text = $"Scan failed: {ex.Message}";
                    StopScan();
                });
    }

    void StopScan()
    {
        _scanSub?.Dispose();
        _scanSub = null;
        _scan.Text = "Scan";
        _status.Text = $"Found {_devices.Count} devices";
    }

    async Task ConnectAsync(IPeripheral peripheral)
    {
        StopScan();
        Disconnect();
        _connected = peripheral;
        _disconnect.IsEnabled = true;
        _status.Text = $"Connecting to {peripheral.Name ?? peripheral.Uuid}...";

        try
        {
            peripheral.Connect(new ConnectionConfig(AutoConnect: false));
            await peripheral
                .WhenStatusChanged()
                .Where(s => s == ConnectionState.Connected)
                .Take(1)
                .Timeout(TimeSpan.FromSeconds(15));

            var reads = 0;
            foreach (var service in await peripheral.GetServices())
            {
                foreach (var characteristic in await peripheral.GetCharacteristics(service.Uuid))
                {
                    if (characteristic.Properties.HasFlag(CharacteristicProperties.Read))
                    {
                        try
                        {
                            await peripheral.ReadCharacteristic(service.Uuid, characteristic.Uuid).Timeout(TimeSpan.FromSeconds(5));
                            reads++;
                        }
                        catch
                        {
                            // recorded by the plugin as an error event; keep reading the rest
                        }
                    }

                    if (characteristic.Properties.HasFlag(CharacteristicProperties.Notify) || characteristic.Properties.HasFlag(CharacteristicProperties.Indicate))
                        _notifySubs.Add(peripheral.NotifyCharacteristic(service.Uuid, characteristic.Uuid).Subscribe(_ => { }, _ => { }));
                }
            }

            _status.Text = $"Connected to {peripheral.Name ?? peripheral.Uuid}: {reads} reads, {_notifySubs.Count} subscriptions";
        }
        catch (Exception ex)
        {
            _status.Text = $"Connect failed: {ex.Message}";
        }
    }

    void Disconnect()
    {
        foreach (var sub in _notifySubs)
            sub.Dispose();
        _notifySubs.Clear();

        _connected?.CancelConnection();
        _connected = null;
        _disconnect.IsEnabled = false;
    }

    sealed class DeviceRow(IPeripheral peripheral, string? localName, int rssi) : BindableObject
    {
        int _rssi = rssi;

        public IPeripheral Peripheral { get; } = peripheral;
        public string Name { get; } = peripheral.Name ?? localName ?? "(unnamed)";
        public string AutomationId { get; } = $"Device_{peripheral.Uuid}";
        public string Detail => $"{Peripheral.Uuid}  rssi {_rssi}";

        public void Update(int rssi)
        {
            _rssi = rssi;
            OnPropertyChanged(nameof(Detail));
        }
    }
}

static class ViewExtensions
{
    public static T Row<T>(this T view, int row) where T : BindableObject
    {
        Grid.SetRow(view, row);
        return view;
    }
}
