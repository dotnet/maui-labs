using Microsoft.Maui.Layouts;
using Microsoft.Maui.Graphics;
using System.Linq;

namespace Comet.Layout
{

	public interface IAutoGrid : ILayout
	{
		void SetupConstraints(View view, ref int currentColumn, ref int currentRow, ref GridConstraints constraint);
	}
	public class GridLayoutManager : ILayoutManager
	{
		private readonly List<GridConstraints> _constraints = new List<GridConstraints>();
		private readonly List<object> _definedRows = new List<object>();
		private readonly List<object> _definedColumns = new List<object>();
		private Size _lastSize;
		private double[] _gridX;
		private double[] _gridY;
		private double[] _widths;
		private double[] _heights;
		private double _width;
		private double _height;

		private readonly double _spacing;
		private readonly AbstractLayout grid;
		readonly IAutoGrid autoGrid;

		public GridLayoutManager(AbstractLayout grid,
			double? spacing)
		{
			this.grid = grid;
			autoGrid = grid as IAutoGrid;
			_spacing = spacing ?? 0;
		}

		public object DefaultRowHeight { get; set; }

		public object DefaultColumnWidth { get; set; }

		public double ColumnSpacing { get; set; }

		public double RowSpacing { get; set; }

		public void Invalidate()
		{
			_constraints.Clear();
			_gridX = null;
			_gridY = null;
			_widths = null;
			_heights = null;
		}

		public Size Measure(double widthConstraint, double heightConstraint)
		{
			var available = new Size(widthConstraint, heightConstraint);
			var layout = grid;
			var childCount = layout.Count;

			// Invalidate stale constraints if children changed
			if (_constraints.Count > 0 && _constraints.Count != childCount)
				Invalidate();

			if (childCount == 0)
				return Size.Zero;

			if (_constraints.Count == 0)
			{
				var maxRow = 0;
				var maxColumn = 0;
				var currentRow = 0;
				var correntColumn = 0;

				for (var index = 0; index < layout.Count; index++)
				{
					var view = layout[index];
					var constraint = GetGridConstraints(view);
					autoGrid?.SetupConstraints(view, ref correntColumn, ref currentRow, ref constraint);
					_constraints.Add(constraint);
					maxRow = Math.Max(maxRow, constraint.Row + constraint.RowSpan - 1);
					maxColumn = Math.Max(maxColumn, constraint.Column + constraint.ColumnSpan - 1);
				}

				while (maxRow >= _definedRows.Count)
					_definedRows.Add(DefaultRowHeight);

				while (maxColumn >= _definedColumns.Count)
					_definedColumns.Add(DefaultColumnWidth);
			}

			if (_gridX is null || !_lastSize.Equals(available))
			{
				ComputeGrid(available.Width, available.Height);
				_lastSize = available;
			}

			for (var index = 0; index < _constraints.Count && index < layout.Count; index++)
			{
				var position = _constraints[index];
				var view = layout[index];

				var x = _gridX[position.Column];
				var y = _gridY[position.Row];

				double w = 0;
				for (var i = 0; i < position.ColumnSpan; i++)
					w += GetColumnWidth(position.Column + i);
				w += ColumnSpacing * Math.Max(0, position.ColumnSpan - 1);

				double h = 0;
				for (var i = 0; i < position.RowSpan; i++)
					h += GetRowHeight(position.Row + i);
				h += RowSpacing * Math.Max(0, position.RowSpan - 1);

				if (position.WeightX < 1 || position.WeightY < 1)
				{
					var viewSize = MeasureChild(view, widthConstraint, heightConstraint);

					var cellWidth = w;
					var cellHeight = h;

					if (position.WeightX <= 0)
						w = viewSize.Width;
					else
						w *= position.WeightX;

					if (position.WeightY <= 0)
						h = viewSize.Height;
					else
						h *= position.WeightY;

					if (position.PositionX > 0)
					{
						var availWidth = cellWidth - w;
						x += (double)Math.Round(availWidth * position.PositionX);
					}

					if (position.PositionY > 0)
					{
						var availHeight = cellHeight - h;
						y += (double)Math.Round(availHeight * position.PositionY);
					}

					view.MeasuredSize = new Size(w, h);
					view.MeasurementValid = true;
				}
				view.MeasuredSize = MeasureChild(view, w, h);
				view.MeasurementValid = true;
			}

			return new Size(_width, _height);
		}

		public Size ArrangeChildren(Rect bounds)
		{
			var layout = grid;
			var measured = bounds.Size;
			var size = bounds.Size;

			// Invalidate stale constraints if children changed
			if (_constraints.Count > 0 && _constraints.Count != layout.Count)
				Invalidate();

			if (layout.Count == 0)
				return measured;

			if (_gridX is null || !_lastSize.Equals(size))
			{
				ComputeGrid(size.Width, size.Height);
				_lastSize = size;
			}

			for (var index = 0; index < _constraints.Count && index < layout.Count; index++)
			{
				var position = _constraints[index];
				var view = layout[index];

				var x = bounds.X + _gridX[position.Column];
				var y = bounds.Y + _gridY[position.Row];

				double w = 0;
				for (var i = 0; i < position.ColumnSpan; i++)
					w += GetColumnWidth(position.Column + i);
				w += ColumnSpacing * Math.Max(0, position.ColumnSpan - 1);

				double h = 0;
				for (var i = 0; i < position.RowSpan; i++)
					h += GetRowHeight(position.Row + i);
				h += RowSpacing * Math.Max(0, position.RowSpan - 1);

				// Alignment is applied inside the resolved grid cell. Measure against that
				// cell rather than the whole grid, otherwise a centered Auto-column layout
				// retains the grid width and overflows back across its star-column sibling.
				var viewSize = MeasureChild(view, w, h);
				view.MeasuredSize = viewSize;
				view.MeasurementValid = true;

				if (position.WeightX < 1 || position.WeightY < 1)
				{
					var cellWidth = w;
					var cellHeight = h;

					if (position.WeightX <= 0)
						w = viewSize.Width;
					else
						w *= position.WeightX;

					if (position.WeightY <= 0)
						h = viewSize.Height;
					else
						h *= position.WeightY;

					if (position.PositionX > 0)
					{
						var availWidth = cellWidth - w;
						x += (double)Math.Round(availWidth * position.PositionX);
					}

					if (position.PositionY > 0)
					{
						var availHeight = cellHeight - h;
						y += (double)Math.Round(availHeight * position.PositionY);
					}
				}
				view.SetFrameFromPlatformView(new Rect(x, y, w, h));
			}
			return measured;
		}

		public int AddRow(object row)
		{
			if (row is null)
				return -1;

			_definedRows.Add(row);
			Invalidate();

			return _definedRows.Count - 1;
		}

		public void AddRows(params object[] rows)
		{
			if (rows is null)
				return;

			foreach (var row in rows)
				_definedRows.Add(row ?? DefaultRowHeight);

			Invalidate();
		}

		public void SetRowHeight(int index, object value)
		{
			if (index >= 0 && index < _definedRows.Count)
			{
				_definedRows[index] = value;
				Invalidate();
			}
		}

		public int AddColumn(object column)
		{
			if (column is null)
				return -1;

			_definedColumns.Add(column);

			Invalidate();
			return _definedColumns.Count - 1;
		}

		public void AddColumns(params object[] columns)
		{
			if (columns is null)
				return;

			foreach (var column in columns)
				_definedColumns.Add(column ?? DefaultColumnWidth);

			Invalidate();
		}

		public void SetColumnWidth(int index, object value)
		{
			if (index >= 0 && index < _definedColumns.Count)
			{
				_definedColumns[index] = value;
				Invalidate();
			}
		}

		private double GetColumnWidth(int column)
		{
			return _widths[column];
		}

		private double GetRowHeight(int row)
		{
			return _heights[row];
		}

		private static Size MeasureChild(View view, double widthConstraint, double heightConstraint)
		{
			var backendView = view.Node is not null ? view : view.BuiltView ?? view;
			var margin = view.GetMargin();
			var outerWidthConstraint = double.IsInfinity(widthConstraint)
				? widthConstraint
				: Math.Max(0, widthConstraint - margin.HorizontalThickness);
			var outerHeightConstraint = double.IsInfinity(heightConstraint)
				? heightConstraint
				: Math.Max(0, heightConstraint - margin.VerticalThickness);

			Size size;
			if (backendView is AbstractLayout)
			{
				// The backend engine owns a layout's complete outer box: Build applies its
				// padding and min/max constraints, so its measured size already includes padding.
				// Passing the outer constraint and adding padding again here doubled padded Auto
				// rows (the source footer's 48pt padding became 96pt).
				size = double.IsInfinity(outerWidthConstraint)
					? Comet.Backend.CometBackendLayoutEngine.Measure(backendView)
					: Comet.Backend.CometBackendLayoutEngine.MeasureContent(
						backendView,
						outerWidthConstraint);
			}
			else if (backendView.Node is { } node)
			{
				// A native leaf measures only its content. Grid owns the leaf's outer-box
				// contract, so remove padding from the constraint and add it exactly once.
				var padding = view.GetPadding();
				var contentWidthConstraint = double.IsInfinity(outerWidthConstraint)
					? outerWidthConstraint
					: Math.Max(0, outerWidthConstraint - padding.HorizontalThickness);
				var contentHeightConstraint = double.IsInfinity(outerHeightConstraint)
					? outerHeightConstraint
					: Math.Max(0, outerHeightConstraint - padding.VerticalThickness);
				size = node.Measure(contentWidthConstraint, contentHeightConstraint);
				size.Width += padding.HorizontalThickness;
				size.Height += padding.VerticalThickness;
			}
			else
			{
				// Legacy/unmaterialized views retain their existing IView measurement path,
				// which already owns frame constraints and margins.
				return view.Measure(widthConstraint, heightConstraint);
			}

			var platformView = (Microsoft.Maui.IView)view;
			size.Width = LayoutManager.ResolveConstraints(
				outerWidthConstraint,
				platformView.Width,
				size.Width,
				platformView.MinimumWidth,
				platformView.MaximumWidth);
			size.Height = LayoutManager.ResolveConstraints(
				outerHeightConstraint,
				platformView.Height,
				size.Height,
				platformView.MinimumHeight,
				platformView.MaximumHeight);

			size.Width += margin.HorizontalThickness;
			size.Height += margin.VerticalThickness;
			return size;
		}

		private static GridConstraints GetGridConstraints(View view)
			=> view.GetLayoutConstraints() as GridConstraints
				?? view.BuiltView?.GetLayoutConstraints() as GridConstraints
				?? GridConstraints.Default;

		private static bool IsAutoSize(object definition)
		{
			var str = definition?.ToString() ?? "";
			return str.Equals("Auto", StringComparison.OrdinalIgnoreCase);
		}

		private void ComputeGrid(double width, double height)
		{
			var rows = _definedRows.Count;
			var columns = _definedColumns.Count;

			_gridX = new double[columns];
			_gridY = new double[rows];
			_widths = new double[columns];
			_heights = new double[rows];
			_width = 0;
			_height = 0;

			double takenX = 0;
			var calculatedColumns = new List<int>();
			var calculatedColumnFactors = new List<double>();
			var autoColumns = new HashSet<int>();
			var starColumns = new HashSet<int>();
			for (var c = 0; c < columns; c++)
			{
				var w = _definedColumns[c];
				if (IsAutoSize(w))
				{
					// Auto columns: start at 0, will be expanded by child measurement
					autoColumns.Add(c);
					_widths[c] = 0;
				}
				else if (!w.ToString().EndsWith("*", StringComparison.Ordinal))
				{
					if (double.TryParse(w.ToString(), out var value))
					{
						takenX += value;
						_widths[c] = value;
					}
					else
					{
						starColumns.Add(c);
						calculatedColumns.Add(c);
						calculatedColumnFactors.Add(GetFactor(w));
					}
				}
				else
				{
					starColumns.Add(c);
					calculatedColumns.Add(c);
					calculatedColumnFactors.Add(GetFactor(w));
				}
			}

			// For Auto columns, measure children to determine width
			if (autoColumns.Count > 0)
			{
				for (var index = 0; index < _constraints.Count && index < grid.Count; index++)
				{
					var constraint = _constraints[index];
					if (Enumerable.Range(constraint.Column, constraint.ColumnSpan).Any(autoColumns.Contains))
					{
						var child = grid[index];
						// Auto columns need the child's intrinsic width. Measuring a layout child
						// at the grid width pins it to that width, consuming the whole row and
						// leaving a sibling star column negative (for example "*", "Auto" with
						// a trailing HStack).
						var childSize = MeasureChild(child, double.PositiveInfinity, height);
						var autoInSpan = Enumerable.Range(constraint.Column, constraint.ColumnSpan)
							.Where(autoColumns.Contains).ToArray();
						if (autoInSpan.Length == 0 ||
							Enumerable.Range(constraint.Column, constraint.ColumnSpan).Any(starColumns.Contains))
							continue;
						var occupied = Enumerable.Range(constraint.Column, constraint.ColumnSpan)
							.Sum(i => _widths[i]) + ColumnSpacing * Math.Max(0, constraint.ColumnSpan - 1);
						var extra = Math.Max(0, childSize.Width - occupied) / autoInSpan.Length;
						foreach (var autoColumn in autoInSpan)
							_widths[autoColumn] += extra;
					}
				}
				foreach (var c in autoColumns)
					takenX += _widths[c];
			}

			var unboundedWidth = double.IsInfinity(width) || double.IsNaN(width);
			if (unboundedWidth)
			{
				MeasureUnboundedStarColumns(height, starColumns);
			}
			else
			{
				var availableWidth = width - takenX -
					(ColumnSpacing * (calculatedColumns.Count > 0 ? columns - 1 : Math.Max(0, columns - 1)));
				var columnFactor = calculatedColumnFactors.Sum(f => f);
				var columnWidth = columnFactor > 0 ? availableWidth / columnFactor : 0;
				var factorIndex = 0;
				foreach (var c in calculatedColumns)
					_widths[c] = columnWidth * calculatedColumnFactors[factorIndex++];
			}

			double takenY = 0;
			var calculatedRows = new List<int>();
			var calculatedRowFactors = new List<double>();
			var autoRows = new HashSet<int>();
			var starRows = new HashSet<int>();
			for (var r = 0; r < rows; r++)
			{
				var h = _definedRows[r];
				if (IsAutoSize(h))
				{
					// Auto rows: start at 0, will be expanded by child measurement
					autoRows.Add(r);
					_heights[r] = 0;
				}
				else if (!h.ToString().EndsWith("*", StringComparison.Ordinal))
				{
					if (double.TryParse(h.ToString(), out var value))
					{
						takenY += value;
						_heights[r] = value;
					}
					else
					{
						starRows.Add(r);
						calculatedRows.Add(r);
						calculatedRowFactors.Add(GetFactor(h));
					}
				}
				else
				{
					starRows.Add(r);
					calculatedRows.Add(r);
					calculatedRowFactors.Add(GetFactor(h));
				}
			}

			// For Auto rows, measure children to determine height
			if (autoRows.Count > 0)
			{
				for (var index = 0; index < _constraints.Count && index < grid.Count; index++)
				{
					var constraint = _constraints[index];
					if (Enumerable.Range(constraint.Row, constraint.RowSpan).Any(autoRows.Contains))
					{
						var child = grid[index];
						var childSize = MeasureChild(child, width, height);
						var autoInSpan = Enumerable.Range(constraint.Row, constraint.RowSpan)
							.Where(autoRows.Contains).ToArray();
						if (autoInSpan.Length == 0 ||
							Enumerable.Range(constraint.Row, constraint.RowSpan).Any(starRows.Contains))
							continue;
						var occupied = Enumerable.Range(constraint.Row, constraint.RowSpan)
							.Sum(i => _heights[i]) + RowSpacing * Math.Max(0, constraint.RowSpan - 1);
						var extra = Math.Max(0, childSize.Height - occupied) / autoInSpan.Length;
						foreach (var autoRow in autoInSpan)
							_heights[autoRow] += extra;
					}
				}
				foreach (var r in autoRows)
					takenY += _heights[r];
			}

			var unboundedHeight = double.IsInfinity(height) || double.IsNaN(height);
			if (unboundedHeight)
			{
				MeasureUnboundedStarRows(width, starRows);
			}
			else
			{
				var availableHeight = height - takenY -
					(RowSpacing * (calculatedRows.Count > 0 ? rows - 1 : Math.Max(0, rows - 1)));
				var rowFactor = calculatedRowFactors.Sum(f => f);
				var rowHeight = rowFactor > 0 ? availableHeight / rowFactor : 0;
				var factorIndex = 0;
				foreach (var r in calculatedRows)
					_heights[r] = rowHeight * calculatedRowFactors[factorIndex++];
			}

			double x = 0;
			for (var c = 0; c < columns; c++)
			{
				_gridX[c] = x;
				x += _widths[c] + (c < columns - 1 ? ColumnSpacing : 0);
			}

			double y = 0;
			for (var r = 0; r < rows; r++)
			{
				_gridY[r] = y;
				y += _heights[r] + (r < rows - 1 ? RowSpacing : 0);
			}

			_width = _widths.Sum() + ColumnSpacing * Math.Max(0, columns - 1);
			_height = _heights.Sum() + RowSpacing * Math.Max(0, rows - 1);
		}

		void MeasureUnboundedStarColumns(double heightConstraint, HashSet<int> starColumns)
		{
			for (var index = 0; index < _constraints.Count && index < grid.Count; index++)
			{
				var constraint = _constraints[index];
				var stars = Enumerable.Range(constraint.Column, constraint.ColumnSpan)
					.Where(starColumns.Contains)
					.ToArray();
				if (stars.Length == 0)
					continue;

				var measured = MeasureChild(grid[index], double.PositiveInfinity, heightConstraint);
				var occupied = Enumerable.Range(constraint.Column, constraint.ColumnSpan)
					.Sum(column => _widths[column]) +
					ColumnSpacing * Math.Max(0, constraint.ColumnSpan - 1);
				DistributeIntrinsicStarSize(
					stars,
					Math.Max(0, measured.Width - occupied),
					_widths,
					_definedColumns);
			}
		}

		void MeasureUnboundedStarRows(double widthConstraint, HashSet<int> starRows)
		{
			for (var index = 0; index < _constraints.Count && index < grid.Count; index++)
			{
				var constraint = _constraints[index];
				var stars = Enumerable.Range(constraint.Row, constraint.RowSpan)
					.Where(starRows.Contains)
					.ToArray();
				if (stars.Length == 0)
					continue;

				var spanWidth = Enumerable.Range(constraint.Column, constraint.ColumnSpan)
					.Sum(column => _widths[column]) +
					ColumnSpacing * Math.Max(0, constraint.ColumnSpan - 1);
				var measured = MeasureChild(grid[index], spanWidth, double.PositiveInfinity);
				var occupied = Enumerable.Range(constraint.Row, constraint.RowSpan)
					.Sum(row => _heights[row]) +
					RowSpacing * Math.Max(0, constraint.RowSpan - 1);
				DistributeIntrinsicStarSize(
					stars,
					Math.Max(0, measured.Height - occupied),
					_heights,
					_definedRows);
			}
		}

		void DistributeIntrinsicStarSize(
			int[] starIndexes,
			double extra,
			double[] sizes,
			IReadOnlyList<object> definitions)
		{
			if (extra <= 0 || starIndexes.Length == 0)
				return;

			var factors = starIndexes.Select(index =>
			{
				var factor = GetFactor(definitions[index]);
				return factor > 0 ? factor : 1;
			}).ToArray();
			var totalFactor = factors.Sum();

			for (var index = 0; index < starIndexes.Length; index++)
			{
				sizes[starIndexes[index]] += extra * factors[index] / totalFactor;
			}
		}

		private double GetFactor(object value)
		{
			if (value is not null)
			{
				var str = value.ToString();
				if (str.EndsWith("*", StringComparison.Ordinal))
				{
					str = str.Substring(0, str.Length - 1);
					if (double.TryParse(str, out var f))
					{
						return f;
					}
				}
			}

			return 1;
		}

		public double CalculateWidth()
		{
			double width = 0;

			if (_widths is not null)
			{
				foreach (var value in _widths)
				{
					width += value;
				}
			}
			else
			{
				var columns = _definedColumns.Count;
				for (var c = 0; c < columns; c++)
				{
					var w = _definedColumns[c];
					if (!"*".Equals(w))
					{
						if (double.TryParse(w.ToString(), out var value))
						{
							width += value;
						}
					}
				}
			}

			return width;
		}

		public double CalculateHeight()
		{
			double height = 0;

			if (_heights is not null)
			{
				foreach (var value in _heights)
				{
					height += value;
				}
			}
			else
			{
				var rows = _definedRows.Count;
				for (var r = 0; r < rows; r++)
				{
					var h = _definedRows[r];
					if (!"*".Equals(h))
					{
						if (double.TryParse(h.ToString(), out var value))
						{
							height += value;
						}
					}
				}
			}

			return height;
		}

	}
}
