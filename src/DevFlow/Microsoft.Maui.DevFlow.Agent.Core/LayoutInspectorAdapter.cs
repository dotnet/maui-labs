// Soft adapter that bridges Comet's IYogaLayoutInspector (in Comet.dll) to the
// agent's wire-format LayoutInfo DTO. This file deliberately holds NO compile-time
// reference to Comet — every type is resolved at runtime via reflection and the
// result is cached. When Comet isn't loaded, every call returns null gracefully.

using System;
using System.Collections;
using System.Reflection;
using System.Threading;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal static class LayoutInspectorAdapter
{
    // Cache the lookup so we pay the GetType cost once per process. The interface
    // type may resolve to null if Comet isn't loaded yet — in that case we periodically
    // retry (lazy via the ScanAttempt counter) so apps that load Comet after the
    // agent starts still work.
    private static Type? _inspectorInterface;
    private static bool _scanned;
    private static readonly object _gate = new();

    private static Type? InspectorInterface()
    {
        if (_scanned && _inspectorInterface != null)
            return _inspectorInterface;

        lock (_gate)
        {
            if (_inspectorInterface != null)
                return _inspectorInterface;

            // Try AssemblyQualifiedName first — fast path when Comet was loaded
            // by name resolution.
            _inspectorInterface = Type.GetType("Comet.Layout.IYogaLayoutInspector, Comet", throwOnError: false);

            // Fallback: scan loaded assemblies for one that exports the type.
            if (_inspectorInterface == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type? t;
                    try
                    {
                        t = asm.GetType("Comet.Layout.IYogaLayoutInspector", throwOnError: false);
                    }
                    catch
                    {
                        continue;
                    }
                    if (t != null)
                    {
                        _inspectorInterface = t;
                        break;
                    }
                }
            }

            _scanned = true;
            return _inspectorInterface;
        }
    }

    /// <summary>
    /// If <paramref name="layoutManager"/> implements Comet's IYogaLayoutInspector,
    /// invoke it and project the snapshot into a wire-format <see cref="LayoutInfo"/>.
    /// Returns null when Comet isn't loaded, the manager doesn't implement the
    /// interface, or no measure pass has run yet.
    /// </summary>
    public static LayoutInfo? TryGetLayoutSnapshot(object? layoutManager)
    {
        if (layoutManager is null)
            return null;

        var iface = InspectorInterface();
        if (iface == null || !iface.IsInstanceOfType(layoutManager))
            return null;

        try
        {
            var method = iface.GetMethod("GetLayoutSnapshot", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                return null;

            var snapshot = method.Invoke(layoutManager, null);
            if (snapshot == null)
                return null;

            return Project(snapshot);
        }
        catch
        {
            return null;
        }
    }

    private static LayoutInfo? Project(object snapshot)
    {
        var t = snapshot.GetType();
        var frame = ReadFrame(t.GetProperty("Frame")?.GetValue(snapshot));
        var info = new LayoutInfo
        {
            Frame = frame,
            FlexDirection = ReadString(t, snapshot, "FlexDirection"),
            AlignItems = ReadString(t, snapshot, "AlignItems"),
            JustifyContent = ReadString(t, snapshot, "JustifyContent"),
            AlignContent = ReadString(t, snapshot, "AlignContent"),
            FlexWrap = ReadString(t, snapshot, "FlexWrap"),
        };

        if (t.GetProperty("Children")?.GetValue(snapshot) is IEnumerable children)
        {
            var list = new List<LayoutChildInfo>();
            foreach (var child in children)
            {
                if (child == null) continue;
                var ct = child.GetType();
                list.Add(new LayoutChildInfo
                {
                    ViewTypeName = ReadString(ct, child, "ViewTypeName"),
                    AutomationId = ct.GetProperty("AutomationId")?.GetValue(child) as string,
                    Frame = ReadFrame(ct.GetProperty("Frame")?.GetValue(child)),
                    FlexGrow = ReadFloat(ct, child, "FlexGrow"),
                    FlexShrink = ReadFloat(ct, child, "FlexShrink"),
                    AlignSelf = ReadString(ct, child, "AlignSelf"),
                    PositionType = ReadString(ct, child, "PositionType"),
                });
            }
            info.Children = list;
        }

        return info;
    }

    private static string ReadString(Type t, object instance, string prop)
        => t.GetProperty(prop)?.GetValue(instance) as string ?? "";

    private static float ReadFloat(Type t, object instance, string prop)
    {
        var v = t.GetProperty(prop)?.GetValue(instance);
        return v switch
        {
            float f => f,
            double d => (float)d,
            _ => 0f,
        };
    }

    // YogaLayoutSnapshot.Frame is a ValueTuple<float,float,float,float>.
    // Read fields by name (Item1..Item4) since runtime types preserve them.
    private static LayoutFrameInfo ReadFrame(object? frame)
    {
        if (frame is null) return new LayoutFrameInfo();
        var t = frame.GetType();
        return new LayoutFrameInfo
        {
            X = ToFloat(t.GetField("Item1")?.GetValue(frame)),
            Y = ToFloat(t.GetField("Item2")?.GetValue(frame)),
            Width = ToFloat(t.GetField("Item3")?.GetValue(frame)),
            Height = ToFloat(t.GetField("Item4")?.GetValue(frame)),
        };
    }

    private static float ToFloat(object? v) => v switch
    {
        float f => f,
        double d => (float)d,
        _ => 0f,
    };
}
