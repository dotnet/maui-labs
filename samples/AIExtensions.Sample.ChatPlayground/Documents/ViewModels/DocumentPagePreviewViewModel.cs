using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DocumentExtraction;
using ExtractedDocumentPage = Microsoft.Extensions.DocumentExtraction.DocumentPage;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>One visible page image plus the selectable regions projected onto it.</summary>
public sealed partial class DocumentPagePreviewViewModel : ObservableObject
{
    public DocumentPagePreviewViewModel(
        DocumentPreviewPage preview,
        Action<DocumentResultNode?> selectNode)
    {
        PageNumber = preview.PageNumber;
        Label = preview.Label;
        Image = ImageSource.FromStream(() => new MemoryStream(preview.Data, writable: false));
        SelectNode = new RelayCommand<DocumentResultNode?>(selectNode);
    }

    public int PageNumber { get; }

    public string Label { get; }

    public ImageSource Image { get; }

    public ObservableCollection<DocumentResultNode> Nodes { get; } = [];

    public IRelayCommand<DocumentResultNode?> SelectNode { get; }

    [ObservableProperty] private ExtractedDocumentPage? extractedPage;
    [ObservableProperty] private DocumentResultNode? selectedNode;

    internal void SetExtraction(
        ExtractedDocumentPage? page,
        IEnumerable<DocumentResultNode> nodes)
    {
        Nodes.Clear();
        foreach (var node in nodes)
            Nodes.Add(node);
        ExtractedPage = page;
    }
}
