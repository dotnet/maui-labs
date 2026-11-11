using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>A <see cref="DataTemplate"/> that creates a view of a type, through the app's services when it can.</summary>
/// <remarks>
/// <para>
/// In XAML, <c>{chat:ViewTemplate local:CalculatorCard}</c> creates one. Each view comes from the app's service
/// provider, <see cref="IPlatformApplication.Current"/>'s <see cref="IPlatformApplication.Services"/> (the services
/// built by <c>MauiProgram</c>), like a page created by Shell navigation: a view registered there
/// (<c>builder.Services.AddTransient&lt;CalculatorCard&gt;()</c>) is resolved, and any other view is created with its
/// constructor's parameters resolved from those services. Register views as transient: every block needs its own
/// view. Before the app has started, and in apps without that provider, the view is created with its public
/// parameterless constructor.
/// </para>
/// </remarks>
public sealed class ViewTemplate : DataTemplate
{
    /// <summary>Initializes a new instance of the <see cref="ViewTemplate"/> class.</summary>
    /// <param name="viewType">The type of view to create. It must derive from <see cref="View"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="viewType"/> does not derive from <see cref="View"/>.</exception>
    public ViewTemplate([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type viewType)
        : base(CreateLoader(viewType))
    {
        ViewType = viewType;
    }

    /// <summary>Gets the type of view the template creates.</summary>
    public Type ViewType { get; }

    private static Func<object> CreateLoader([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type viewType)
    {
        ArgumentNullException.ThrowIfNull(viewType);
        if (!typeof(View).IsAssignableFrom(viewType))
            throw new ArgumentException($"{viewType.Name} does not derive from {nameof(View)}.", nameof(viewType));

        return () => CreateView(viewType);
    }

    private static View CreateView([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type viewType)
    {
        if (IPlatformApplication.Current?.Services is { } services)
        {
            try
            {
                return (View)ActivatorUtilities.GetServiceOrCreateInstance(services, viewType);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"Could not create '{viewType.Name}'. Register it, or the services its constructor takes, in the app's services.",
                    ex);
            }
        }

        try
        {
            return (View)Activator.CreateInstance(viewType)!;
        }
        catch (MissingMethodException ex)
        {
            throw new InvalidOperationException(
                $"Could not create '{viewType.Name}': there are no app services to resolve its constructor from, and it has no public parameterless constructor.",
                ex);
        }
    }
}
