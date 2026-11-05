#nullable enable
using System;
using System.IO;
using CometBaristaNotes.Data;

namespace CometBaristaNotes.Services;

public sealed class BaristaAppStorage
{
    readonly object _gate = new();
    readonly string _defaultDatabasePath;
    readonly IPreferencesStore _defaultThemePreferences;
    string? _comparisonDatabasePath;
    bool _resolutionStarted;

    public static BaristaAppStorage Current { get; } =
        new(SqliteDataStore.GetDefaultPath(), new MauiPreferencesStore());

    internal BaristaAppStorage(string defaultDatabasePath, IPreferencesStore defaultThemePreferences)
    {
        _defaultDatabasePath = Path.GetFullPath(defaultDatabasePath);
        _defaultThemePreferences = defaultThemePreferences
            ?? throw new ArgumentNullException(nameof(defaultThemePreferences));
    }

    /// <summary>Select one database before any Barista service or theme access.
    /// The caller supplies the namespace path; this API does not define a fixture format.</summary>
    public void ConfigureComparisonDatabase(string databasePath)
    {
        lock (_gate)
        {
            if (_resolutionStarted)
                throw new InvalidOperationException(
                    "Select comparison storage before resolving Barista services or theme. Use a new process to change namespaces.");
            if (_comparisonDatabasePath is not null)
                throw new InvalidOperationException("Comparison storage has already been selected.");
            ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
            if (!Path.IsPathFullyQualified(databasePath))
                throw new ArgumentException("An absolute comparison database path is required.", nameof(databasePath));

            var path = ResolvePath(databasePath);
            if (Directory.Exists(path) || Path.EndsInDirectorySeparator(path))
                throw new ArgumentException("Select a database file, not a directory.", nameof(databasePath));
            if (IsDefaultDatabasePath(path))
                throw new ArgumentException("Comparison storage cannot use the default BaristaNotes database.", nameof(databasePath));
            _comparisonDatabasePath = path;
        }
    }

    internal void MarkResolutionStarted()
    {
        lock (_gate)
            _resolutionStarted = true;
    }

    internal SqliteDataStore OpenStore()
    {
        lock (_gate)
        {
            _resolutionStarted = true;
            if (_comparisonDatabasePath is null)
                return new SqliteDataStore(_defaultDatabasePath);

            if (IsDefaultDatabasePath(ResolvePath(_comparisonDatabasePath)))
                throw new InvalidOperationException("Comparison storage resolves to the default BaristaNotes database.");
            return new SqliteDataStore(_comparisonDatabasePath, seedOnFirstRun: false);
        }
    }

    internal bool ValidateBinding(IBaristaDataStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        lock (_gate)
        {
            _resolutionStarted = true;
            if (_comparisonDatabasePath is null)
                return false;
            if (store is not SqliteDataStore sqlite
                || !string.Equals(ResolvePath(sqlite.DatabasePath), _comparisonDatabasePath, StringComparison.Ordinal))
                throw new InvalidOperationException("The service store does not match the selected comparison database.");
            return true;
        }
    }

    internal BaristaThemePreferences CreateThemePreferences(IBaristaDataStore store)
        => new(ValidateBinding(store)
            ? ((SqliteDataStore)store).CreatePreferencesStore()
            : _defaultThemePreferences);

    bool IsDefaultDatabasePath(string path)
        => string.Equals(path, ResolvePath(_defaultDatabasePath), StringComparison.OrdinalIgnoreCase);

    internal static string ResolvePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var resolved = Path.GetPathRoot(fullPath)!;
        foreach (var segment in fullPath[resolved.Length..].Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, segment);
            var info = new FileInfo(resolved);
            if (info.LinkTarget is not null)
                resolved = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException($"Cannot resolve the database path link '{resolved}'.");
        }
        return resolved;
    }
}
