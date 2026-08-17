using System.Runtime.InteropServices;
using System.Text;
using static Microsoft.Maui.DevFlow.Driver.Mac.MacAccessibility;

namespace Microsoft.Maui.DevFlow.Driver.Mac;

/// <summary>
/// High-level wrapper around a macOS AXUIElement.
/// Manages CoreFoundation reference counting automatically.
/// Only usable on macOS — all public entry points should guard with OperatingSystem.IsMacOS().
/// </summary>
internal sealed class AXElement : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>
    /// The underlying native handle. For internal use in P/Invoke scenarios.
    /// </summary>
    internal nint Handle => _handle;

    private AXElement(nint handle)
    {
        _handle = handle;
    }

    /// <summary>
    /// Create an AXElement for an application by PID.
    /// The returned element is owned and must be disposed.
    /// </summary>
    public static AXElement CreateForApplication(int pid)
    {
        var h = AXUIElementCreateApplication(pid);
        return h == 0
            ? throw new InvalidOperationException($"Failed to create AXUIElement for PID {pid}")
            : new AXElement(h);
    }

    /// <summary>
    /// Create an AXElement wrapping a non-owned handle (e.g. from CFArrayGetValueAtIndex).
    /// Retains the handle so it stays valid after the parent array is released.
    /// </summary>
    internal static AXElement FromNonOwned(nint handle)
    {
        if (handle == 0) throw new ArgumentException("Null handle", nameof(handle));
        CFRetain(handle);
        return new AXElement(handle);
    }

    // --- Attribute accessors ---

    public string? Role => GetStringAttribute("AXRole");
    public string? Subrole => GetStringAttribute("AXSubrole");
    public string? Title => GetStringAttribute("AXTitle");
    public string? Description => GetStringAttribute("AXDescription");
    public string? Value => GetStringAttribute("AXValue");
    public string? Identifier => GetStringAttribute("AXIdentifier");
    public string? PlaceholderValue => GetStringAttribute("AXPlaceholderValue");

    /// <summary>
    /// Get a string attribute by name. Returns null if not present or not a string.
    /// </summary>
    public string? GetStringAttribute(string name)
    {
        var cfName = CreateCFString(name);
        try
        {
            int err = AXUIElementCopyAttributeValue(_handle, cfName, out var value);
            if (err != kAXErrorSuccess || value == 0) return null;
            try
            {
                return CFGetTypeID(value) == CFStringGetTypeID() ? ReadCFString(value) : null;
            }
            finally { CFRelease(value); }
        }
        finally { CFRelease(cfName); }
    }

    /// <summary>
    /// Get children as a list of AXElements. Each child is retained and must be disposed.
    /// </summary>
    public List<AXElement> GetChildren()
    {
        var result = new List<AXElement>();
        var cfName = CreateCFString("AXChildren");
        try
        {
            int err = AXUIElementCopyAttributeValue(_handle, cfName, out var value);
            if (err != kAXErrorSuccess || value == 0) return result;
            try
            {
                if (CFGetTypeID(value) != CFArrayGetTypeID()) return result;
                var count = CFArrayGetCount(value);
                for (long i = 0; i < count; i++)
                {
                    var child = CFArrayGetValueAtIndex(value, i);
                    if (child != 0) result.Add(FromNonOwned(child));
                }
            }
            finally { CFRelease(value); }
        }
        finally { CFRelease(cfName); }
        return result;
    }

    /// <summary>
    /// Perform an action (e.g., "AXPress", "AXShowMenu").
    /// Returns true on success.
    /// </summary>
    public bool PerformAction(string action)
    {
        var cfAction = CreateCFString(action);
        try
        {
            return AXUIElementPerformAction(_handle, cfAction) == kAXErrorSuccess;
        }
        finally { CFRelease(cfAction); }
    }

    /// <summary>
    /// Press the element (shortcut for PerformAction("AXPress")).
    /// </summary>
    public bool Press() => PerformAction("AXPress");

    /// <summary>
    /// Search recursively for the first element matching a predicate.
    /// Returns a retained element (caller must dispose) or null.
    /// </summary>
    public AXElement? FindFirst(Func<AXElement, bool> predicate, int maxDepth = 15)
        => FindFirstInternal(predicate, 0, maxDepth);

    /// <summary>
    /// Search recursively for all elements matching a predicate.
    /// Returns retained elements (caller must dispose each).
    /// </summary>
    public List<AXElement> FindAll(Func<AXElement, bool> predicate, int maxDepth = 15)
    {
        var results = new List<AXElement>();
        FindAllInternal(predicate, 0, maxDepth, results);
        return results;
    }

    /// <summary>
    /// Dump the accessibility tree as indented text, skipping menu bars.
    /// </summary>
    public string DumpTree(int maxDepth = 12)
    {
        var sb = new StringBuilder();
        DumpTreeInternal(sb, 0, maxDepth);
        return sb.ToString();
    }

    // --- Private helpers ---

    private AXElement? FindFirstInternal(Func<AXElement, bool> predicate, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return null;
        if (predicate(this))
        {
            CFRetain(_handle);
            return new AXElement(_handle);
        }

        var children = GetChildren();
        try
        {
            foreach (var child in children)
            {
                var found = child.FindFirstInternal(predicate, depth + 1, maxDepth);
                if (found is not null)
                    return found;
            }
        }
        finally
        {
            foreach (var child in children) child.Dispose();
        }
        return null;
    }

    private void FindAllInternal(Func<AXElement, bool> predicate, int depth, int maxDepth, List<AXElement> results)
    {
        if (depth >= maxDepth) return;
        if (predicate(this))
        {
            CFRetain(_handle);
            results.Add(new AXElement(_handle));
        }

        var children = GetChildren();
        try
        {
            foreach (var child in children)
                child.FindAllInternal(predicate, depth + 1, maxDepth, results);
        }
        finally
        {
            foreach (var child in children) child.Dispose();
        }
    }

    private void DumpTreeInternal(StringBuilder sb, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return;
        var role = Role ?? "?";
        // Skip menu elements
        if (role is "AXMenuBar" or "AXMenu" or "AXMenuItem" or "AXMenuBarItem") return;

        var indent = new string(' ', depth * 2);
        sb.Append(indent);
        sb.Append(role);
        var sub = Subrole;
        if (!string.IsNullOrEmpty(sub)) sb.Append($" ({sub})");
        var t = Title;
        if (!string.IsNullOrEmpty(t)) sb.Append($" title='{t}'");
        var d = Description;
        if (!string.IsNullOrEmpty(d)) sb.Append($" desc='{d}'");
        var v = Value;
        if (!string.IsNullOrEmpty(v)) sb.Append($" val='{v}'");
        var id = Identifier;
        if (!string.IsNullOrEmpty(id)) sb.Append($" id='{id}'");
        sb.AppendLine();

        var children = GetChildren();
        try
        {
            foreach (var child in children)
                child.DumpTreeInternal(sb, depth + 1, maxDepth);
        }
        finally
        {
            foreach (var child in children) child.Dispose();
        }
    }

    // --- CF Helpers ---

    internal static nint CreateCFString(string s)
        => CFStringCreateWithCString(0, s, kCFStringEncodingUTF8);

    internal static string? ReadCFString(nint cfStr)
    {
        if (cfStr == 0) return null;
        var len = CFStringGetLength(cfStr);
        var bufSize = len * 4 + 1;
        var buf = Marshal.AllocHGlobal((int)bufSize);
        try
        {
            return CFStringGetCString(cfStr, buf, bufSize, kCFStringEncodingUTF8)
                ? Marshal.PtrToStringUTF8(buf)
                : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // --- IDisposable ---

    public void Dispose()
    {
        if (!_disposed && _handle != 0)
        {
            CFRelease(_handle);
            _handle = 0;
            _disposed = true;
        }
    }
}
