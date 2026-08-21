using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.Essentials.AI;
using Microsoft.Maui.Graphics;

namespace DocumentExtractionSample;

internal sealed class DocumentOverlayDrawable : IDrawable
{
	private IReadOnlyList<OverlayRegion> _regions = [];
	private float _sourceWidth = 1;
	private float _sourceHeight = 1;

	internal void SetPage(DocumentPage? page)
	{
		if (page?.CoordinateUnit != DocumentCoordinateUnit.Normalized ||
			page.CoordinateOrigin != DocumentCoordinateOrigin.BottomLeft)
		{
			_regions = [];
			return;
		}

		_sourceWidth = GetDimension(page, "apple.sourcePixelWidth") ?? 1;
		_sourceHeight = GetDimension(page, "apple.sourcePixelHeight") ?? 1;
		var regions = new List<OverlayRegion>();
		AppendElements(regions, page.Elements);
		_regions = regions;
	}

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		if (_regions.Count == 0)
		{
			return;
		}

		var sourceAspect = _sourceWidth / _sourceHeight;
		var targetAspect = dirtyRect.Width / dirtyRect.Height;
		RectF imageRect;
		if (targetAspect > sourceAspect)
		{
			var width = dirtyRect.Height * sourceAspect;
			imageRect = new RectF(
				dirtyRect.X + ((dirtyRect.Width - width) / 2),
				dirtyRect.Y,
				width,
				dirtyRect.Height);
		}
		else
		{
			var height = dirtyRect.Width / sourceAspect;
			imageRect = new RectF(
				dirtyRect.X,
				dirtyRect.Y + ((dirtyRect.Height - height) / 2),
				dirtyRect.Width,
				height);
		}

		canvas.StrokeSize = 2;
		foreach (var region in _regions)
		{
			if (region.Polygon.Count < 2)
			{
				continue;
			}

			canvas.StrokeColor = region.Color;
			var path = new PathF();
			var first = ToViewPoint(region.Polygon[0], imageRect);
			path.MoveTo(first);
			for (var index = 1; index < region.Polygon.Count; index++)
			{
				path.LineTo(ToViewPoint(region.Polygon[index], imageRect));
			}
			path.Close();
			canvas.DrawPath(path);
		}
	}

	private static PointF ToViewPoint(DocumentPoint point, RectF imageRect) =>
		new(
			imageRect.X + (point.X * imageRect.Width),
			imageRect.Y + ((1 - point.Y) * imageRect.Height));

	private static float? GetDimension(DocumentPage page, string key)
	{
		if (page.AdditionalProperties?.TryGetValue(key, out var value) != true)
		{
			return null;
		}

		return value switch
		{
			int number => number,
			long number => number,
			float number => number,
			double number => (float)number,
			_ => null,
		};
	}

	private static void AppendElements(
		List<OverlayRegion> regions,
		IReadOnlyList<DocumentElement> elements)
	{
		foreach (var element in elements)
		{
			if (element.BoundingRegion is { } region)
			{
				regions.Add(new OverlayRegion(region.Polygon, GetColor(element)));
			}

			switch (element)
			{
				case DocumentTable { Cells: { } cells }:
					foreach (var cell in cells)
					{
						if (cell.BoundingRegion is { } cellRegion)
						{
							regions.Add(new OverlayRegion(cellRegion.Polygon, Colors.Orange));
						}
						if (cell.Elements is { } cellElements)
						{
							AppendElements(regions, cellElements);
						}
					}
					break;
				case AppleListElement list:
					AppendElements(regions, list.Items);
					break;
				case AppleListItemElement item when item.Elements.Count > 0:
					AppendElements(regions, item.Elements);
					break;
			}
		}
	}

	private static Color GetColor(DocumentElement element) =>
		element switch
		{
			DocumentTable => Colors.OrangeRed,
			AppleListElement => Colors.MediumPurple,
			AppleListItemElement => Colors.Purple,
			AppleBarcodeElement => Colors.LimeGreen,
			DocumentBlock => Colors.DeepSkyBlue,
			_ => Colors.Yellow,
		};

	private sealed record OverlayRegion(
		IReadOnlyList<DocumentPoint> Polygon,
		Color Color);
}
