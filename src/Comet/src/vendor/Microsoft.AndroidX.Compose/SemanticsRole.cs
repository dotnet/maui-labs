namespace AndroidX.Compose;

/// <summary>
/// Mirror of Kotlin's <c>androidx.compose.ui.semantics.Role</c> — the
/// accessibility role advertised to TalkBack via
/// <c>Modifier.semantics { role = ... }</c>. Compose defines
/// <c>Role</c> as a <c>@JvmInline value class</c> over an <see cref="int"/>;
/// the underlying values match Kotlin's source order
/// (<see cref="Button"/> = 0 … <see cref="Carousel"/> = 8) and have
/// been stable since the feature was introduced.
///
/// Use with <see cref="Modifier.Semantics(SemanticsRole)"/> or
/// <see cref="Modifier.Semantics(string?, SemanticsRole?)"/> to attach a
/// role to any composable that the framework didn't already classify
/// (e.g. tagging a custom <see cref="Box"/> as <see cref="Button"/>).
/// </summary>
public enum SemanticsRole
{
    /// <summary>An element that triggers an action when clicked.</summary>
    Button = 0,
    /// <summary>An on/off two-state checkbox.</summary>
    Checkbox = 1,
    /// <summary>An on/off two-state switch.</summary>
    Switch = 2,
    /// <summary>A radio button that is part of a mutually-exclusive group.</summary>
    RadioButton = 3,
    /// <summary>A selectable tab in a tab strip.</summary>
    Tab = 4,
    /// <summary>A static image (announced as "image" by TalkBack).</summary>
    Image = 5,
    /// <summary>An element that opens a dropdown / popup menu of choices.</summary>
    DropdownList = 6,
    /// <summary>An incremental value picker (e.g. date/time spinner).</summary>
    ValuePicker = 7,
    /// <summary>A horizontally swipeable carousel of items.</summary>
    Carousel = 8,
}
