using Foundation;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// File provider that serves static content from the macOS app bundle's Resources directory.
/// </summary>
internal class MacOSMauiAssetFileProvider : IFileProvider
{
    private readonly string _contentRoot;

    public MacOSMauiAssetFileProvider(string contentRoot)
    {
        _contentRoot = contentRoot;
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        return NotFoundDirectoryContents.Singleton;
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        if (string.IsNullOrEmpty(subpath))
            return new NotFoundFileInfo(subpath);

        subpath = subpath.TrimStart('/');
        var resourcePath = Path.Combine(_contentRoot, subpath);

        // Try NSBundle resource lookup
        var bundlePath = NSBundle.MainBundle.PathForResource(
            Path.GetFileNameWithoutExtension(resourcePath),
            Path.GetExtension(resourcePath).TrimStart('.'),
            Path.GetDirectoryName(resourcePath));

        if (bundlePath != null && File.Exists(bundlePath))
            return new BundleFileInfo(bundlePath);

        // Try direct path in bundle resources
        var directRoot = Path.GetFullPath(Path.Combine(NSBundle.MainBundle.ResourcePath, _contentRoot));
        var directPath = Path.GetFullPath(Path.Combine(directRoot, subpath));
        if (IsPathUnderRoot(directPath, directRoot) && File.Exists(directPath))
            return new BundleFileInfo(directPath);

        return new NotFoundFileInfo(subpath);
    }

    public IChangeToken Watch(string filter)
    {
        return NullChangeToken.Singleton;
    }

    static bool IsPathUnderRoot(string path, string root)
    {
        var rootWithSeparator = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;

        return path.StartsWith(rootWithSeparator, StringComparison.Ordinal);
    }

    sealed class BundleFileInfo : IFileInfo
    {
        private readonly FileInfo _fileInfo;

        public BundleFileInfo(string path) => _fileInfo = new FileInfo(path);

        public bool Exists => _fileInfo.Exists;
        public long Length => _fileInfo.Length;
        public string? PhysicalPath => _fileInfo.FullName;
        public string Name => _fileInfo.Name;
        public DateTimeOffset LastModified => _fileInfo.LastWriteTimeUtc;
        public bool IsDirectory => false;

        public Stream CreateReadStream() => _fileInfo.OpenRead();
    }
}
