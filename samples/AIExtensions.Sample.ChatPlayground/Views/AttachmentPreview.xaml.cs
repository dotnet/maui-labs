using System.Windows.Input;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Shares the selected-file preview and removal action across playground composers.</summary>
public partial class AttachmentPreview : ContentView
{
    public static readonly BindableProperty PreviewSourceProperty = BindableProperty.Create(
        nameof(PreviewSource), typeof(ImageSource), typeof(AttachmentPreview));
    public static readonly BindableProperty PreviewAspectProperty = BindableProperty.Create(
        nameof(PreviewAspect), typeof(Aspect), typeof(AttachmentPreview), Aspect.AspectFill);
    public static readonly BindableProperty FileNameProperty = BindableProperty.Create(
        nameof(FileName), typeof(string), typeof(AttachmentPreview), string.Empty);
    public static readonly BindableProperty RemoveCommandProperty = BindableProperty.Create(
        nameof(RemoveCommand), typeof(ICommand), typeof(AttachmentPreview));
    public static readonly BindableProperty RemoveDescriptionProperty = BindableProperty.Create(
        nameof(RemoveDescription), typeof(string), typeof(AttachmentPreview), "Remove attachment");
    public static readonly BindableProperty NameAutomationIdProperty = BindableProperty.Create(
        nameof(NameAutomationId), typeof(string), typeof(AttachmentPreview));
    public static readonly BindableProperty RemoveAutomationIdProperty = BindableProperty.Create(
        nameof(RemoveAutomationId), typeof(string), typeof(AttachmentPreview));

    public AttachmentPreview() => InitializeComponent();

    public ImageSource? PreviewSource
    {
        get => (ImageSource?)GetValue(PreviewSourceProperty);
        set => SetValue(PreviewSourceProperty, value);
    }

    public Aspect PreviewAspect
    {
        get => (Aspect)GetValue(PreviewAspectProperty);
        set => SetValue(PreviewAspectProperty, value);
    }

    public string FileName
    {
        get => (string)GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    public ICommand? RemoveCommand
    {
        get => (ICommand?)GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    public string RemoveDescription
    {
        get => (string)GetValue(RemoveDescriptionProperty);
        set => SetValue(RemoveDescriptionProperty, value);
    }

    public string? NameAutomationId
    {
        get => (string?)GetValue(NameAutomationIdProperty);
        set => SetValue(NameAutomationIdProperty, value);
    }

    public string? RemoveAutomationId
    {
        get => (string?)GetValue(RemoveAutomationIdProperty);
        set => SetValue(RemoveAutomationIdProperty, value);
    }
}
