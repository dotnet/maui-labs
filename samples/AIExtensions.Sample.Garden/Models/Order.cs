namespace AIExtensions.Sample.Garden.Models;

/// <summary>
/// A committed shopping order in the singleton archive.
/// Created by <c>checkout_list</c> after the user approves the transition.
/// </summary>
public record Order(
    string Id,
    DateTime PlacedAt,
    IReadOnlyList<ListItem> Items)
{
    public decimal Total => Items.Sum(i => i.Subtotal);
}
