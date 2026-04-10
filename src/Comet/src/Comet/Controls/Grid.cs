using System.Collections.Generic;
using Comet.Layout;
using Microsoft.Maui;
using Microsoft.Maui.Layouts;

namespace Comet
{
	public class Grid : AbstractLayout
	{
		public Grid(
			object[] columns = null,
			object[] rows = null,
			float? spacing = null,
			float? columnSpacing = null,
			float? rowSpacing = null,
			object defaultRowHeight = null,
			object defaultColumnWidth = null)
		{
			Spacing = spacing;
			var layout = (Layout.GridLayoutManager)LayoutManager;

			layout.DefaultRowHeight = defaultRowHeight ?? "*";
			layout.DefaultColumnWidth = defaultColumnWidth ?? "*";
			layout.ColumnSpacing = columnSpacing ?? spacing ?? 0;
			layout.RowSpacing = rowSpacing ?? spacing ?? 0;

			if (columns is not null)
				layout.AddColumns(columns);

			if (rows is not null)
				layout.AddRows(rows);
		}

		public float? Spacing { get; }


		protected override ILayoutManager CreateLayoutManager() => new Comet.Layout.GridLayoutManager(this, Spacing);
	}
}
