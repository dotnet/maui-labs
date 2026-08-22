using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Services;

public sealed class GardenAdaptiveContextFactory(AdaptiveComponentCatalogBuilder catalogBuilder)
{
    public AdaptiveSurfaceContext Create(
        AdaptiveSurfaceSession session,
        AdaptiveSurfaceDescriptor surface,
        IReadOnlyList<AdaptiveDataDescriptor> dataManifest,
        PresentationIntentContext presentation,
        string stateSignature,
        double pageWidth,
        double pageHeight)
    {
        var display = DeviceDisplay.Current.MainDisplayInfo;
        return new()
        {
            SurfaceInstanceId = session.SurfaceInstanceId,
            Surface = surface,
            DataManifest = dataManifest,
            ComponentCatalog = catalogBuilder.Build(
                session.StateRoot,
                dataManifest,
                surface.Regions.Select(region => region.Name).ToArray()),
            Viewport = new()
            {
                Width = pageWidth > 0 ? pageWidth : display.Width / display.Density,
                Height = pageHeight > 0 ? pageHeight : display.Height / display.Density,
                Density = display.Density,
                Idiom = DeviceInfo.Current.Idiom.ToString(),
                Orientation = display.Orientation.ToString(),
            },
            Intent = presentation.Intent,
            RecentContext = presentation.RecentUserContext,
            StateSignature = stateSignature,
        };
    }
}
