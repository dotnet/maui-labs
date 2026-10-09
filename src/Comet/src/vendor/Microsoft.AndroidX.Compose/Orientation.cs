namespace AndroidX.Compose;

/// <summary>
/// Drag / scroll orientation. Mirrors Kotlin's
/// <c>androidx.compose.foundation.gestures.Orientation</c> enum. Passed
/// to <see cref="Modifier.Draggable(DraggableState, Orientation, bool)"/>
/// (and to any future orientation-aware modifier helpers) to pick the
/// axis the gesture operates on.
/// </summary>
public enum Orientation
{
    /// <summary>Vertical axis — drag amounts are deltas in the Y direction.</summary>
    Vertical,

    /// <summary>Horizontal axis — drag amounts are deltas in the X direction.</summary>
    Horizontal,
}
