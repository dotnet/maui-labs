using System.Windows.Input;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.Graphics;
using ExtractedDocumentPage = Microsoft.Extensions.DocumentExtraction.DocumentPage;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Draws selectable normalized document geometry over an image preview.</summary>
public sealed class DocumentOverlayView : GraphicsView
{
    public static readonly BindableProperty PageProperty = BindableProperty.Create(
        nameof(Page),
        typeof(ExtractedDocumentPage),
        typeof(DocumentOverlayView),
        propertyChanged: static (bindable, _, _) => ((DocumentOverlayView)bindable).Refresh());

    public static readonly BindableProperty NodesProperty = BindableProperty.Create(
        nameof(Nodes),
        typeof(IReadOnlyList<DocumentResultNode>),
        typeof(DocumentOverlayView),
        propertyChanged: static (bindable, _, _) => ((DocumentOverlayView)bindable).Refresh());

    public static readonly BindableProperty SelectedNodeProperty = BindableProperty.Create(
        nameof(SelectedNode),
        typeof(DocumentResultNode),
        typeof(DocumentOverlayView),
        propertyChanged: static (bindable, _, _) => ((DocumentOverlayView)bindable).Refresh());

    public static readonly BindableProperty SelectionCommandProperty = BindableProperty.Create(
        nameof(SelectionCommand),
        typeof(ICommand),
        typeof(DocumentOverlayView));

    private readonly OverlayDrawable _drawable = new();

    public DocumentOverlayView()
    {
        Drawable = _drawable;
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);
    }

    public ExtractedDocumentPage? Page
    {
        get => (ExtractedDocumentPage?)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public IReadOnlyList<DocumentResultNode>? Nodes
    {
        get => (IReadOnlyList<DocumentResultNode>?)GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public DocumentResultNode? SelectedNode
    {
        get => (DocumentResultNode?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public ICommand? SelectionCommand
    {
        get => (ICommand?)GetValue(SelectionCommandProperty);
        set => SetValue(SelectionCommandProperty, value);
    }

    private void Refresh()
    {
        _drawable.SetState(Page, Nodes, SelectedNode);
        Invalidate();
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (e.GetPosition(this) is not { } point)
            return;

        var node = _drawable.HitTest(
            new PointF((float)point.X, (float)point.Y),
            new RectF(0, 0, (float)Width, (float)Height));
        if (node is not null && SelectionCommand?.CanExecute(node) != false)
            SelectionCommand?.Execute(node);
    }

    private sealed class OverlayDrawable : IDrawable
    {
        private IReadOnlyList<DocumentResultNode> _nodes = [];
        private DocumentResultNode? _selectedNode;
        private float _sourceWidth = 1;
        private float _sourceHeight = 1;

        internal void SetState(
            ExtractedDocumentPage? page,
            IReadOnlyList<DocumentResultNode>? nodes,
            DocumentResultNode? selectedNode)
        {
            _nodes = nodes ?? [];
            _selectedNode = selectedNode;
            _sourceWidth = GetDimension(page, "apple.sourcePixelWidth") ?? 1;
            _sourceHeight = GetDimension(page, "apple.sourcePixelHeight") ?? 1;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var imageRect = GetImageRect(dirtyRect);
            foreach (var node in _nodes.Where(static node => node.BoundingRegion is not null))
            {
                if (ReferenceEquals(node, _selectedNode))
                    continue;
                DrawNode(canvas, node, imageRect, selected: false);
            }
            if (_selectedNode?.BoundingRegion is not null)
                DrawNode(canvas, _selectedNode, imageRect, selected: true);
        }

        internal DocumentResultNode? HitTest(PointF point, RectF dirtyRect)
        {
            var imageRect = GetImageRect(dirtyRect);
            if (!imageRect.Contains(point))
                return null;

            var normalized = new PointF(
                (point.X - imageRect.X) / imageRect.Width,
                1 - ((point.Y - imageRect.Y) / imageRect.Height));
            return _nodes
                .Select(static node => (Node: node, Bounds: node.BoundingRegion?.GetBounds()))
                .Where(item =>
                    item.Bounds is { } bounds &&
                    normalized.X >= bounds.Left &&
                    normalized.X <= bounds.Right &&
                    normalized.Y >= bounds.Top &&
                    normalized.Y <= bounds.Bottom)
                .OrderBy(static item =>
                    (item.Bounds!.Value.Right - item.Bounds.Value.Left) *
                    (item.Bounds.Value.Bottom - item.Bounds.Value.Top))
                .Select(static item => item.Node)
                .FirstOrDefault();
        }

        private void DrawNode(
            ICanvas canvas,
            DocumentResultNode node,
            RectF imageRect,
            bool selected)
        {
            var polygon = node.BoundingRegion?.Polygon;
            if (polygon is null || polygon.Count < 2)
                return;

            canvas.StrokeColor = selected ? Colors.Yellow : GetColor(node.RegionKind);
            canvas.StrokeSize = selected ? 5 : 2;
            var path = new PathF();
            path.MoveTo(ToViewPoint(polygon[0], imageRect));
            for (var index = 1; index < polygon.Count; index++)
                path.LineTo(ToViewPoint(polygon[index], imageRect));
            path.Close();
            canvas.DrawPath(path);
        }

        private RectF GetImageRect(RectF dirtyRect)
        {
            var sourceAspect = _sourceWidth / _sourceHeight;
            var targetAspect = dirtyRect.Width / dirtyRect.Height;
            if (targetAspect > sourceAspect)
            {
                var width = dirtyRect.Height * sourceAspect;
                return new(
                    dirtyRect.X + ((dirtyRect.Width - width) / 2),
                    dirtyRect.Y,
                    width,
                    dirtyRect.Height);
            }

            var height = dirtyRect.Width / sourceAspect;
            return new(
                dirtyRect.X,
                dirtyRect.Y + ((dirtyRect.Height - height) / 2),
                dirtyRect.Width,
                height);
        }

        private static PointF ToViewPoint(DocumentPoint point, RectF imageRect) =>
            new(
                imageRect.X + (point.X * imageRect.Width),
                imageRect.Y + ((1 - point.Y) * imageRect.Height));

        private static float? GetDimension(ExtractedDocumentPage? page, string key)
        {
            if (page?.AdditionalProperties?.TryGetValue(key, out var value) != true)
                return null;

            return value switch
            {
                int number => number,
                long number => number,
                float number => number,
                double number => (float)number,
                _ => null,
            };
        }

        private static Color GetColor(DocumentRegionKind kind) =>
            kind switch
            {
                DocumentRegionKind.Table => Colors.OrangeRed,
                DocumentRegionKind.Cell => Colors.Orange,
                DocumentRegionKind.List => Colors.MediumPurple,
                DocumentRegionKind.ListItem => Colors.Purple,
                DocumentRegionKind.Barcode => Colors.LimeGreen,
                DocumentRegionKind.Block => Colors.DeepSkyBlue,
                DocumentRegionKind.Image => Colors.Cyan,
                _ => Colors.White,
            };
    }
}
