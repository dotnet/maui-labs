namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>AlertDialog</c>. The first composable in the facade to
/// use <em>named slot properties</em> instead of a single collection
/// initializer: <c>ConfirmButton</c> is required, and any of
/// <c>DismissButton</c>, <c>Icon</c>, <c>Title</c>, and <c>Text</c> may
/// be supplied via C# object-initializer syntax. Slots left <c>null</c>
/// are reported as defaulted through the <c>$default</c> bitmask so
/// Compose substitutes the Kotlin defaults.
///
/// <code>
/// new AlertDialog(onDismissRequest: () => show.Value = false)
/// {
///     Title         = new Text("Confirm?"),
///     Text          = new Text("This cannot be undone."),
///     ConfirmButton = new Button(onClick: ...) { new Text("OK") },
///     DismissButton = new Button(onClick: ...) { new Text("Cancel") },
/// }
/// </code>
///
/// This shape is the template that <c>ModalBottomSheet</c>,
/// <c>DatePickerDialog</c>, <c>TimePickerDialog</c>, and the tooltip
/// composables will follow once their <c>*State</c> bridges land.
/// </summary>
public sealed partial class AlertDialog;
