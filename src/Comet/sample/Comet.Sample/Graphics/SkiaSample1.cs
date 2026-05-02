using System;
using System.Collections.Generic;
using System.Text;
using static Comet.CometControls;

namespace Comet.Samples
{
	/// <summary>
	/// This example how to use use a DrawableControl directly: you give it a control delegate
	/// in it's constructor.
	/// </summary>
	public class SkiaSample1 : Component
	{
				public override View Render() => new SimpleFingerPaint();

	}
}
