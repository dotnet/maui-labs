#nullable enable
#if DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(ControlGallery.Services.HotReloadService))]
namespace ControlGallery.Services
{
    public static class HotReloadService
    {
        public static event Action<Type[]?>? UpdateApplicationEvent;

        internal static void ClearCache(Type[]? types) { }
        internal static void UpdateApplication(Type[]? types)
        {
            //if ((types != null && types.Length > 0 && types[0] != null))
            //{
            UpdateApplicationEvent?.Invoke(types);
            //}
        }
    }
}
#endif