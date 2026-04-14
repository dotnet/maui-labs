using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
#if WPFAPP
using Microsoft.Maui.Media;
#endif

namespace Sample;

public class StoragePage : ContentPage
{
    const string PrefKey = "test_pref";
    const string SecureKey = "test_secret";

    Label _prefValueLabel;
    Label _secureValueLabel;
    Label _statusLabel;
    Label _clipboardLabel;
    Entry _prefEntry;
    Entry _secureEntry;
    Entry _clipboardEntry;
#if WPFAPP
    Label _pickerResultLabel;
#endif
    int _counter;

    public StoragePage()
    {
        var layout = new VerticalStackLayout { Padding = 20, Spacing = 10 };

        _statusLabel = new Label { FontSize = 14, TextColor = Color.FromArgb("#FFD93D"), Text = "Ready" };
        layout.Children.Add(_statusLabel);

        // Preferences section
        layout.Children.Add(CreateHeader("Preferences"));

        _prefValueLabel = new Label
        {
            FontSize = 16,
            Text = $"Stored: {TryGet(() => Preferences.Get(PrefKey, "(not set)"))}"
        }.WithPrimaryText();
        layout.Children.Add(_prefValueLabel);

        _prefEntry = new Entry { Placeholder = "Enter a value" }.WithEntryTheme();
        layout.Children.Add(_prefEntry);

        var prefSaveBtn = new Button { Text = "Save Preference" };
        prefSaveBtn.Clicked += OnSavePref;
        layout.Children.Add(prefSaveBtn);

        var prefCounterBtn = new Button { Text = "Increment Counter" };
        prefCounterBtn.Clicked += OnIncrementCounter;
        layout.Children.Add(prefCounterBtn);

        var prefClearBtn = new Button { Text = "Clear Preferences" };
        prefClearBtn.Clicked += OnClearPrefs;
        layout.Children.Add(prefClearBtn);

        // Secure Storage section
        layout.Children.Add(CreateHeader("Secure Storage"));

        _secureValueLabel = new Label
        {
            FontSize = 16,
            Text = "Stored: (loading...)"
        }.WithPrimaryText();
        layout.Children.Add(_secureValueLabel);
        _ = LoadSecureValue();

        _secureEntry = new Entry { Placeholder = "Enter a secret" }.WithEntryTheme();
        layout.Children.Add(_secureEntry);

        var secureSaveBtn = new Button { Text = "Save Secret" };
        secureSaveBtn.Clicked += OnSaveSecret;
        layout.Children.Add(secureSaveBtn);

        var secureRemoveBtn = new Button { Text = "Remove Secret" };
        secureRemoveBtn.Clicked += OnRemoveSecret;
        layout.Children.Add(secureRemoveBtn);

        var secureRemoveAllBtn = new Button { Text = "Remove All Secrets" };
        secureRemoveAllBtn.Clicked += OnRemoveAllSecrets;
        layout.Children.Add(secureRemoveAllBtn);

        // Load counter
        try { _counter = Preferences.Get("test_counter", 0); }
        catch { _counter = 0; }

        // Clipboard section
        layout.Children.Add(CreateHeader("Clipboard"));

        _clipboardLabel = new Label
        {
            FontSize = 16,
            Text = "Clipboard: (empty)"
        }.WithPrimaryText();
        layout.Children.Add(_clipboardLabel);

        _clipboardEntry = new Entry { Placeholder = "Text to copy" }.WithEntryTheme();
        layout.Children.Add(_clipboardEntry);

        var clipCopyBtn = new Button { Text = "Copy to Clipboard" };
        clipCopyBtn.Clicked += OnCopyClipboard;
        layout.Children.Add(clipCopyBtn);

        var clipPasteBtn = new Button { Text = "Paste from Clipboard" };
        clipPasteBtn.Clicked += OnPasteClipboard;
        layout.Children.Add(clipPasteBtn);

#if WPFAPP
        // File Picker section
        layout.Children.Add(CreateHeader("File Picker"));

        _pickerResultLabel = new Label
        {
            FontSize = 16,
            Text = "No file selected"
        }.WithPrimaryText();
        layout.Children.Add(_pickerResultLabel);

        var pickFileBtn = new Button { Text = "Pick File" };
        pickFileBtn.Clicked += OnPickFile;
        layout.Children.Add(pickFileBtn);

        var pickMultiBtn = new Button { Text = "Pick Multiple Files" };
        pickMultiBtn.Clicked += OnPickMultipleFiles;
        layout.Children.Add(pickMultiBtn);

        // Media Picker section
        layout.Children.Add(CreateHeader("Media Picker"));

        var pickPhotoBtn = new Button { Text = "Pick Photo" };
        pickPhotoBtn.Clicked += OnPickPhoto;
        layout.Children.Add(pickPhotoBtn);

        var pickVideoBtn = new Button { Text = "Pick Video" };
        pickVideoBtn.Clicked += OnPickVideo;
        layout.Children.Add(pickVideoBtn);
#endif

        Content = new ScrollView { Content = layout };
    }

    void OnSavePref(object? sender, EventArgs e)
    {
        var val = _prefEntry?.Text;
        if (string.IsNullOrEmpty(val))
        {
            _statusLabel.Text = "Enter a value first";
            return;
        }
        try
        {
            Preferences.Set(PrefKey, val);
            _prefValueLabel.Text = $"Stored: {val}";
            _statusLabel.Text = $"Preference saved: {PrefKey} = {val}";
        }
        catch (Exception ex) { _statusLabel.Text = $"Error: {ex.Message}"; }
    }

    void OnIncrementCounter(object? sender, EventArgs e)
    {
        _counter++;
        try { Preferences.Set("test_counter", _counter); }
        catch { /* static API unavailable */ }
        _statusLabel.Text = $"Counter: {_counter} (persisted)";
    }

    void OnClearPrefs(object? sender, EventArgs e)
    {
        try { Preferences.Clear(); }
        catch { /* static API unavailable */ }
        _counter = 0;
        _prefValueLabel.Text = "Stored: (not set)";
        _statusLabel.Text = "All preferences cleared";
    }

    async void OnSaveSecret(object? sender, EventArgs e)
    {
        var val = _secureEntry?.Text;
        if (string.IsNullOrEmpty(val))
        {
            _statusLabel.Text = "Enter a secret first";
            return;
        }
        try
        {
            await SecureStorage.SetAsync(SecureKey, val);
            _secureValueLabel.Text = $"Stored: {val}";
            _statusLabel.Text = $"Secret saved to Keychain";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    async void OnRemoveSecret(object? sender, EventArgs e)
    {
        try
        {
            var removed = SecureStorage.Remove(SecureKey);
            _secureValueLabel.Text = "Stored: (not set)";
            _statusLabel.Text = removed ? "Secret removed" : "No secret found to remove";
        }
        catch (Exception ex) { _statusLabel.Text = $"Error: {ex.Message}"; }
    }

    async void OnRemoveAllSecrets(object? sender, EventArgs e)
    {
        try
        {
            SecureStorage.RemoveAll();
            _secureValueLabel.Text = "Stored: (not set)";
            _statusLabel.Text = "All secrets removed";
        }
        catch (Exception ex) { _statusLabel.Text = $"Error: {ex.Message}"; }
    }

    async Task LoadSecureValue()
    {
        try
        {
            var val = await SecureStorage.GetAsync(SecureKey);
            _secureValueLabel.Text = $"Stored: {val ?? "(not set)"}";
        }
        catch (Exception ex)
        {
            _secureValueLabel.Text = $"Error: {ex.Message}";
        }
    }

    async void OnCopyClipboard(object? sender, EventArgs e)
    {
        var text = _clipboardEntry?.Text;
        if (string.IsNullOrEmpty(text))
        {
            _statusLabel.Text = "Enter text to copy first";
            return;
        }
        try
        {
            await Clipboard.SetTextAsync(text);
            _clipboardLabel.Text = $"Copied: {text}";
            _statusLabel.Text = "Text copied to clipboard";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    async void OnPasteClipboard(object? sender, EventArgs e)
    {
        try
        {
            var text = await Clipboard.GetTextAsync();
            _clipboardLabel.Text = text is not null ? $"Pasted: {text}" : "Clipboard: (empty)";
            _statusLabel.Text = text is not null ? "Text pasted from clipboard" : "Clipboard is empty";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    static Label CreateHeader(string text) => new Label
    {
        Text = text,
        FontSize = 22,
        FontAttributes = FontAttributes.Bold,
        Margin = new Thickness(0, 15, 0, 5)
    }.WithSectionStyle();

    static string TryGet(Func<string> getter)
    {
        try { return getter(); }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

#if WPFAPP
    async void OnPickFile(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.PickAsync();
            if (result is not null)
            {
                _pickerResultLabel.Text = $"Picked: {result.FileName}";
                _statusLabel.Text = $"File: {result.FullPath}";
            }
            else
            {
                _statusLabel.Text = "File picking cancelled";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    async void OnPickMultipleFiles(object? sender, EventArgs e)
    {
        try
        {
            var results = await FilePicker.PickMultipleAsync();
            var files = results?.Where(r => r is not null).ToList();
            if (files?.Count > 0)
            {
                _pickerResultLabel.Text = $"Picked {files.Count} file(s): {string.Join(", ", files.Select(f => f!.FileName))}";
                _statusLabel.Text = $"Multiple files picked";
            }
            else
            {
                _statusLabel.Text = "File picking cancelled";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    async void OnPickPhoto(object? sender, EventArgs e)
    {
        try
        {
#pragma warning disable CS0618
            var result = await MediaPicker.PickPhotoAsync();
#pragma warning restore CS0618
            if (result is not null)
            {
                _pickerResultLabel.Text = $"Photo: {result.FileName}";
                _statusLabel.Text = $"Photo: {result.FullPath}";
            }
            else
            {
                _statusLabel.Text = "Photo picking cancelled";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    async void OnPickVideo(object? sender, EventArgs e)
    {
        try
        {
#pragma warning disable CS0618
            var result = await MediaPicker.PickVideoAsync();
#pragma warning restore CS0618
            if (result is not null)
            {
                _pickerResultLabel.Text = $"Video: {result.FileName}";
                _statusLabel.Text = $"Video: {result.FullPath}";
            }
            else
            {
                _statusLabel.Text = "Video picking cancelled";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }
#endif
}

