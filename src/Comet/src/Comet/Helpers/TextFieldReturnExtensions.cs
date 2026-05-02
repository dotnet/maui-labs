using System;
using System.Windows.Input;

namespace Comet
{
	/// <summary>
	/// Extension methods for TextField/Entry return key behavior.
	/// </summary>
	public static class TextFieldReturnExtensions
	{
		internal const string ReturnTypeKey = "Entry.ReturnType";
		internal const string ReturnCommandKey = "Entry.ReturnCommand";
		internal const string ReturnCommandParameterKey = "Entry.ReturnCommandParameter";

		/// <summary>
		/// Sets the return key type for the soft keyboard.
		/// </summary>
		public static T ReturnType<T>(this T view, ReturnType returnType, bool cascades = true) where T : View =>
			view.SetEnvironment(ReturnTypeKey, returnType, cascades);

		/// <summary>
		/// Sets the command to execute when the return key is pressed.
		/// </summary>
		public static T ReturnCommand<T>(this T view, ICommand command, bool cascades = true) where T : View =>
			view.SetEnvironment(ReturnCommandKey, command, cascades);

		/// <summary>
		/// Sets the command to execute when the return key is pressed using an Action.
		/// </summary>
		public static T ReturnCommand<T>(this T view, Action action, bool cascades = true) where T : View =>
			view.SetEnvironment(ReturnCommandKey, new SimpleCommand(action), cascades);

		/// <summary>
		/// Sets the parameter for the return command.
		/// </summary>
		public static T ReturnCommandParameter<T>(this T view, object parameter, bool cascades = true) where T : View =>
			view.SetEnvironment(ReturnCommandParameterKey, parameter, cascades);

		/// <summary>
		/// Gets the return type for a view.
		/// </summary>
		public static ReturnType GetReturnType(this View view) =>
			view.GetEnvironment<ReturnType?>(ReturnTypeKey) ?? Comet.ReturnType.Default;

		/// <summary>
		/// Gets the return command for a view.
		/// </summary>
		public static ICommand GetReturnCommand(this View view) =>
			view.GetEnvironment<ICommand>(ReturnCommandKey);

		/// <summary>
		/// Gets the return command parameter for a view.
		/// </summary>
		public static object GetReturnCommandParameter(this View view) =>
			view.GetEnvironment<object>(ReturnCommandParameterKey);
	}

	/// <summary>
	/// Specifies the type of return key on the soft keyboard.
	/// </summary>
	public enum ReturnType
	{
		Default,
		Done,
		Go,
		Next,
		Search,
		Send
	}

	/// <summary>
	/// Simple ICommand implementation for use with return key actions.
	/// </summary>
	internal class SimpleCommand : ICommand
	{
		private readonly Action _execute;
		private readonly Func<bool> _canExecute;

		public SimpleCommand(Action execute, Func<bool> canExecute = null)
		{
			_execute = execute ?? throw new ArgumentNullException(nameof(execute));
			_canExecute = canExecute;
		}

		public event EventHandler CanExecuteChanged;

		public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

		public void Execute(object parameter) => _execute();

		public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}
}
