namespace AIExtensions.Sample.ChatPlayground;

public partial class DocumentComposer : ContentView
{
    public DocumentComposer() => InitializeComponent();

    public event EventHandler? ReadRequested;

    private void ReadClicked(object? sender, EventArgs e) => ReadRequested?.Invoke(this, e);
}
