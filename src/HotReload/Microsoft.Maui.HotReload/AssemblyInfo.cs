#if !NETSTANDARD
using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Microsoft.Maui.Labs.HotReload.MauiMetadataUpdateHandler))]
#endif
