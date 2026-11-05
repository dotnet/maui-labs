#nullable enable
using System;
using System.Collections.Generic;
using Comet.Backend;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend;

public class BackendMeasureInvalidationTests
{
	[Fact]
	public void ColorUpdate_ReplayedTextProperties_KeepCachedMeasurement()
	{
		var cache = new BackendMeasureCache();
		int calls = 0;
		Size Measure(double w, double h) { calls++; return new Size(40, 20); }
		void Replay(Color color)
		{
			cache.ApplyProperty(PropertyIds.Text_Value, PropertyValue.From("text"));
			cache.ApplyProperty(PropertyIds.Text_FontSize, PropertyValue.From(16d));
			cache.ApplyProperty(PropertyIds.Padding, PropertyValue.FromObject(new Thickness(4)));
			cache.ApplyProperty(PropertyIds.CornerRadius, PropertyValue.FromObject(default(CornerRadii)));
			cache.ApplyProperty(PropertyIds.ClipShape, PropertyValue.From(false));
			cache.ApplyProperty(PropertyIds.Text_Color, PropertyValue.From(color));
		}
		Replay(Colors.Red);
		_ = cache.GetOrMeasure(100, 100, Measure);
		for (int i = 0; i < 100; i++)
		{
			Replay(i % 2 == 0 ? Colors.Blue : Colors.Red);
			_ = cache.GetOrMeasure(100, 100, Measure);
		}
		Assert.Equal(1, calls);
	}

	[Fact]
	public void GeometryConstraintsAndExplicitInvalidation_Remeasure()
	{
		var cache = new BackendMeasureCache();
		int calls = 0;
		Size Measure(double w, double h) { calls++; return new Size(w, h); }
		cache.ApplyProperty(PropertyIds.Text_FontSize, PropertyValue.From(16d));
		_ = cache.GetOrMeasure(100, 50, Measure);
		cache.ApplyProperty(PropertyIds.Text_FontSize, PropertyValue.From(20d));
		_ = cache.GetOrMeasure(100, 50, Measure);
		_ = cache.GetOrMeasure(120, 50, Measure);
		cache.Clear();
		_ = cache.GetOrMeasure(120, 50, Measure);
		Assert.Equal(4, calls);
	}

	[Fact]
	public void MutablePayloadAndUnknownProperty_AlwaysInvalidate()
	{
		var cache = new BackendMeasureCache();
		var runs = new List<TextRun>();
		int calls = 0;
		Size Measure(double w, double h) { calls++; return Size.Zero; }
		for (int i = 0; i < 2; i++)
		{
			cache.ApplyProperty(PropertyIds.Text_Runs, PropertyValue.FromObject(runs));
			_ = cache.GetOrMeasure(100, 50, Measure);
		}
		for (int i = 0; i < 2; i++)
		{
			cache.ApplyProperty(new PropertyId(60000), PropertyValue.From(0));
			_ = cache.GetOrMeasure(100, 50, Measure);
		}
		Assert.Equal(4, calls);
	}
}
