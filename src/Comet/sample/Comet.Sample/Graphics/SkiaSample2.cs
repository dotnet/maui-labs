using System;
using System.Collections.Generic;
using System.Text;
using static Comet.CometControls;

namespace Comet.Samples
{
	/// <summary>
	/// This example shows the cleaner way to use a drawable control.  A control delegate can be
	/// implicitly converted into a drawable control, so there is no need to wrap it like in
	/// SkiaSample1.
	/// </summary>
	public class SkiaSample2 : Component
	{
				public override View Render() => new SimpleFingerPaint();

	}
}
