using System;

namespace Comet
{
	/// <summary>
	/// Marks a MAUI interface as having stateful styling support.
	/// The source generator uses this to emit configuration structs,
	/// scoped style extensions, and ResolveCurrentStyle() methods.
	/// Place alongside [CometGenerate] attributes in ControlsGenerator.cs.
	/// </summary>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class CometControlStateAttribute : Attribute
	{
		public CometControlStateAttribute(Type interfaceType)
		{
			InterfaceType = interfaceType;
		}

		/// <summary>
		/// The MAUI interface type (e.g., typeof(IButton)).
		/// </summary>
		public Type InterfaceType { get; }

		/// <summary>
		/// Interactive state names for this control.
		/// Example: new[] { "IsPressed", "IsHovered", "IsFocused" }
		/// </summary>
		public string[] States { get; set; }

		/// <summary>
		/// Override the generated control name. Defaults to interface name minus the 'I' prefix.
		/// Required when CometGenerate uses a custom ClassName (e.g., ISwitch → Toggle).
		/// </summary>
		public string ControlName { get; set; }

		/// <summary>
		/// Additional configuration struct properties as "Name:Type:SourceField" triples.
		/// SourceField is the Binding property name on the generated control.
		/// Example: new[] { "Label:string:Text" } → config.Label = text?.CurrentValue
		/// If SourceField is omitted, it defaults to Name.
		/// </summary>
		public string[] ConfigProperties { get; set; }
	}
}
