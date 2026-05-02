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
					var constraint = view.GetLayoutConstraints() as GridConstraints ?? GridConstraints.Default;
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

				double h = 0;
				for (var i = 0; i < position.RowSpan; i++)
					h += GetRowHeight(position.Row + i);

				if (position.WeightX < 1 || position.WeightY < 1)
				{
					var viewSize = view.Measure(widthConstraint, heightConstraint);

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
				view.Measure(w, h);
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

				var viewSize =  view.Measure(measured.Width, measured.Height);

				var x = _gridX[position.Column];
				var y = _gridY[position.Row];

				double w = 0;
				for (var i = 0; i < position.ColumnSpan; i++)
					w += GetColumnWidth(position.Column + i);

				double h = 0;
				for (var i = 0; i < position.RowSpan; i++)
					h += GetRowHeight(position.Row + i);

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
						calculatedColumns.Add(c);
						calculatedColumnFactors.Add(GetFactor(w));
					}
				}
				else
				{
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
					if (autoColumns.Contains(constraint.Column))
					{
						var child = grid[index];
						var childSize = child.Measure(width, height);
						_widths[constraint.Column] = Math.Max(_widths[constraint.Column], childSize.Width);
					}
				}
				foreach (var c in autoColumns)
					takenX += _widths[c];
			}

			var availableWidth = width - takenX - (ColumnSpacing * (calculatedColumns.Count > 0 ? columns - 1 : Math.Max(0, columns - 1)));
			if (double.IsInfinity(availableWidth) || double.IsNaN(availableWidth))
				availableWidth = 0;
			var columnFactor = calculatedColumnFactors.Sum(f => f);
			var columnWidth = columnFactor > 0 ? availableWidth / columnFactor : 0;
			var factorIndex = 0;
			foreach (var c in calculatedColumns)
			{
				_widths[c] = columnWidth * calculatedColumnFactors[factorIndex++];
			}

			double takenY = 0;
			var calculatedRows = new List<int>();
			var calculatedRowFactors = new List<double>();
			var autoRows = new HashSet<int>();
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
						calculatedRows.Add(r);
						calculatedRowFactors.Add(GetFactor(h));
					}
				}
				else
				{
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
					if (autoRows.Contains(constraint.Row))
					{
						var child = grid[index];
						var childSize = child.Measure(width, height);
						_heights[constraint.Row] = Math.Max(_heights[constraint.Row], childSize.Height);
					}
				}
				foreach (var r in autoRows)
					takenY += _heights[r];
			}

			var availableHeight = height - takenY - (RowSpacing * (calculatedRows.Count > 0 ? rows - 1 : Math.Max(0, rows - 1)));
			if (double.IsInfinity(availableHeight) || double.IsNaN(availableHeight))
				availableHeight = 0;
			var rowFactor = calculatedRowFactors.Sum(f => f);
			var rowHeight = rowFactor > 0 ? availableHeight / rowFactor : 0;
			factorIndex = 0;
			foreach (var r in calculatedRows)
			{
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
