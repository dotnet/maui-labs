#nullable enable

using Microsoft.Maui.Controls;
using PlatformView = Microsoft.Maui.Platforms.Windows.WPF.LayoutPanel;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class LayoutHandler
	{
		public static IPropertyMapper<Layout, LayoutHandler> Mapper = new PropertyMapper<Layout, LayoutHandler>(ViewMapper)
		{
			[nameof(ILayout.Background)] = MapBackground,
			[nameof(ILayout.ClipsToBounds)] = MapClipsToBounds,
#if ANDROID || WINDOWS
			[nameof(IView.InputTransparent)] = MapInputTransparent,
#endif
		};

		public static CommandMapper<Layout, LayoutHandler> CommandMapper = new(ViewCommandMapper)
		{
			[nameof(ILayoutHandler.Add)] = MapAdd,
			[nameof(ILayoutHandler.Remove)] = MapRemove,
			[nameof(ILayoutHandler.Clear)] = MapClear,
			[nameof(ILayoutHandler.Insert)] = MapInsert,
			[nameof(ILayoutHandler.Update)] = MapUpdate,
			[nameof(ILayoutHandler.UpdateZIndex)] = MapUpdateZIndex,
		};

		public LayoutHandler() : base(Mapper, CommandMapper)
		{
		}

		public LayoutHandler(IPropertyMapper? mapper = null, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{

		}

		public static void MapBackground(LayoutHandler handler, ILayout layout)
		{
			if (handler.PlatformView == null) return;
			var bg = layout.Background;
			if (bg is SolidPaint solid && solid.Color != null)
			{
				var c = solid.Color;
				handler.PlatformView.Background = new System.Windows.Media.SolidColorBrush(
					System.Windows.Media.Color.FromArgb(
						(byte)(c.Alpha * 255), (byte)(c.Red * 255),
						(byte)(c.Green * 255), (byte)(c.Blue * 255)));
			}
			else if (bg == null)
			{
				handler.PlatformView.Background = null;
			}
		}

		public static void MapClipsToBounds(LayoutHandler handler, ILayout layout)
		{
			if (handler.PlatformView == null) return;
			handler.PlatformView.ClipToBounds = layout.ClipsToBounds;
		}

		public static void MapAdd(LayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
			{
				handler.Add(args.View);
			}
		}

		public static void MapRemove(LayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
			{
				handler.Remove(args.View);
			}
		}

		public static void MapInsert(LayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
			{
				handler.Insert(args.Index, args.View);
			}
		}

		public static void MapClear(LayoutHandler handler, ILayout layout, object? arg)
		{
			handler.Clear();
		}

		static void MapUpdate(LayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
			{
				handler.Update(args.Index, args.View);
			}
		}

		static void MapUpdateZIndex(LayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is IView view)
			{
				handler.UpdateZIndex(view);
			}
		}

		/// <summary>
		/// Converts a FlowDirection to the appropriate FlowDirection for cross-platform layout 
		/// </summary>
		/// <param name="flowDirection"></param>
		/// <returns>The FlowDirection to assume for cross-platform layout</returns>
		internal static FlowDirection GetLayoutFlowDirection(FlowDirection flowDirection)
		{
#if WINDOWS
			// The native LayoutPanel in Windows will automagically flip our layout coordinates if it's in RTL mode.
			// So for cross-platform layout purposes, we just always treat things as being LTR and let the Panel sort out the rest.
			return FlowDirection.LeftToRight;
#else
			return flowDirection;
#endif
		}
	}
}
