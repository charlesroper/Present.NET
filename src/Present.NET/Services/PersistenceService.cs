using System.IO;

namespace Present.NET.Services;

public enum ThemePreference
{
    System,
    Light,
    Dark
}

/// <summary>
/// Handles saving and restoring the slide URL list to/from disk.
/// Auto-save location: %APPDATA%\Present.NET\slides.txt
/// </summary>
public static class PersistenceService
{
    private static readonly string LegacyAppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Present");

    private static readonly string DefaultAppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Present.NET");

    private static string _appDataDir = DefaultAppDataDir;

    private static string DefaultSavePath => Path.Combine(_appDataDir, "slides.txt");
    private static string ThemePreferencePath => Path.Combine(_appDataDir, "theme.txt");

    /// <summary>
    /// Load URLs from the auto-save file. Returns empty list if not found.
    /// </summary>
    public static List<string> LoadDefault()
    {
        if (File.Exists(DefaultSavePath))
            return LoadFrom(DefaultSavePath);

        if (_appDataDir == DefaultAppDataDir)
        {
            var legacyPath = Path.Combine(LegacyAppDataDir, "slides.txt");
            if (File.Exists(legacyPath))
                return LoadFrom(legacyPath);
        }

        return new List<string>();
    }

    /// <summary>
    /// Save URLs to the auto-save file.
    /// </summary>
    public static void SaveDefault(IEnumerable<string> urls)
    {
        Directory.CreateDirectory(_appDataDir);
        SaveTo(DefaultSavePath, urls);
    }

    public static ThemePreference LoadThemePreference()
    {
        if (!File.Exists(ThemePreferencePath))
            return ThemePreference.System;

        var raw = File.ReadAllText(ThemePreferencePath).Trim();
        return raw.ToLowerInvariant() switch
        {
            "light" => ThemePreference.Light,
            "dark" => ThemePreference.Dark,
            "system" => ThemePreference.System,
            _ => ThemePreference.System
        };
    }

    public static void SaveThemePreference(ThemePreference preference)
    {
        Directory.CreateDirectory(_appDataDir);
        var serialized = preference switch
        {
            ThemePreference.Light => "light",
            ThemePreference.Dark => "dark",
            _ => "system"
        };

        File.WriteAllText(ThemePreferencePath, serialized);
    }

    internal static void ConfigureStorageRootForTesting(string appDataDir)
    {
        _appDataDir = appDataDir;
    }

    internal static void ResetStorageRootForTesting()
    {
        _appDataDir = DefaultAppDataDir;
    }

    /// <summary>
    /// Load URLs from a specified file path (one URL per line).
    /// </summary>
    public static List<string> LoadFrom(string path)
    {
        return File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Save URLs to a specified file path (one URL per line).
    /// </summary>
    public static void SaveTo(string path, IEnumerable<string> urls)
    {
        File.WriteAllLines(path, urls);
    }
}
