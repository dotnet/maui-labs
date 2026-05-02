using System;

namespace Comet.Styles
{
	/// <summary>
	/// Configuration provided to ButtonStyle implementations.
	/// TargetView carries the view reference so styles can resolve tokens
	/// against the nearest scoped theme.
	/// </summary>
	public readonly struct ButtonConfiguration
	{
		public View TargetView { get; init; }
		public bool IsPressed { get; init; }
		public bool IsHovered { get; init; }
		public bool IsEnabled { get; init; }
		public bool IsFocused { get; init; }
		public string Label { get; init; }
	}

	/// <summary>
	/// Configuration provided to ToggleStyle implementations.
	/// </summary>
	public readonly struct ToggleConfiguration
	{
		public View TargetView { get; init; }
		public bool IsOn { get; init; }
		public bool IsEnabled { get; init; }
		public bool IsFocused { get; init; }
	}

	/// <summary>
	/// Configuration provided to TextFieldStyle implementations.
	/// </summary>
	public readonly struct TextFieldConfiguration
	{
		public View TargetView { get; init; }
		public bool IsEditing { get; init; }
		public bool IsEnabled { get; init; }
		public bool IsFocused { get; init; }
		public string Placeholder { get; init; }
	}

	/// <summary>
	/// Configuration provided to SliderStyle implementations.
	/// </summary>
	public readonly struct SliderConfiguration
	{
		public View TargetView { get; init; }
		public double Value { get; init; }
		public double Minimum { get; init; }
		public double Maximum { get; init; }
		public bool IsEnabled { get; init; }
		public bool IsDragging { get; init; }
	}
}
