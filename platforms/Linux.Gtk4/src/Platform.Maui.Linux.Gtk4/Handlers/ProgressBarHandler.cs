using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class ProgressBarHandler : GtkViewHandler<IProgress, Gtk.ProgressBar>
{
	public static new IPropertyMapper<IProgress, ProgressBarHandler> Mapper =
		new PropertyMapper<IProgress, ProgressBarHandler>(ViewMapper)
		{
			[nameof(IProgress.Progress)] = MapProgress,
			[nameof(IView.Background)] = MapProgressColor,
			["ProgressColor"] = MapProgressBarColor,
		};

	public ProgressBarHandler() : base(Mapper)
	{
	}

	protected override Gtk.ProgressBar CreatePlatformView()
	{
		var bar = Gtk.ProgressBar.New();
		bar.SetShowText(false);
		return bar;
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		var size = base.GetDesiredSize(widthConstraint, heightConstraint);
		// GTK ProgressBar has a very small natural height; ensure minimum visibility
		return new Size(size.Width, Math.Max(size.Height, 8));
	}

	public static void MapProgress(ProgressBarHandler handler, IProgress progress)
	{
		handler.PlatformView?.SetFraction(progress.Progress);
	}

	static void MapProgressColor(ProgressBarHandler handler, IProgress progress)
	{
		if (progress is IView view && view.Background is SolidPaint solidPaint && solidPaint.Color != null)
		{
			handler.ApplyCss(handler.PlatformView, $"background-color: {ToGtkColor(solidPaint.Color)};");
		}
	}

	public static void MapProgressBarColor(ProgressBarHandler handler, IProgress progress)
	{
		if (progress is Microsoft.Maui.Controls.ProgressBar pb && pb.ProgressColor != null)
			handler.ApplyCssWithSelector(handler.PlatformView, "* > trough > progress",
				$"background-color: {ToGtkColor(pb.ProgressColor)};");
	}
}
