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
        private float _coordinateWidth = 1;
        private float _coordinateHeight = 1;
        private float _imageWidth = 1;
        private float _imageHeight = 1;
        private DocumentCoordinateOrigin _coordinateOrigin = DocumentCoordinateOrigin.TopLeft;

        internal void SetState(
            ExtractedDocumentPage? page,
            IReadOnlyList<DocumentResultNode>? nodes,
            DocumentResultNode? selectedNode)
        {
            _nodes = nodes ?? [];
            _selectedNode = selectedNode;
            var normalized = page?.CoordinateUnit == DocumentCoordinateUnit.Normalized;
            _imageWidth = GetDimension(page, "apple.sourcePixelWidth") ??
                page?.Dimensions?.Width ??
                1;
            _imageHeight = GetDimension(page, "apple.sourcePixelHeight") ??
                page?.Dimensions?.Height ??
                1;
            _coordinateWidth = normalized ? 1 : _imageWidth;
            _coordinateHeight = normalized ? 1 : _imageHeight;
            _coordinateOrigin = page?.CoordinateOrigin ?? DocumentCoordinateOrigin.TopLeft;
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

            var normalizedX = (point.X - imageRect.X) / imageRect.Width;
            var normalizedY = (point.Y - imageRect.Y) / imageRect.Height;
            var sourcePoint = new PointF(
                normalizedX * _coordinateWidth,
                (_coordinateOrigin == DocumentCoordinateOrigin.BottomLeft
                    ? 1 - normalizedY
                    : normalizedY) * _coordinateHeight);
            return _nodes
                .Select(static node => (Node: node, Bounds: node.BoundingRegion?.GetBounds()))
                .Where(item =>
                    item.Bounds is { } bounds &&
                    sourcePoint.X >= bounds.Left &&
                    sourcePoint.X <= bounds.Right &&
                    sourcePoint.Y >= bounds.Top &&
                    sourcePoint.Y <= bounds.Bottom)
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
            var sourceAspect = _imageWidth / _imageHeight;
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

        private PointF ToViewPoint(DocumentPoint point, RectF imageRect) =>
            new(
                imageRect.X + ((point.X / _coordinateWidth) * imageRect.Width),
                imageRect.Y +
                    ((_coordinateOrigin == DocumentCoordinateOrigin.BottomLeft
                        ? 1 - (point.Y / _coordinateHeight)
                        : point.Y / _coordinateHeight) * imageRect.Height));

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
