using System.IO;

namespace Present.Services;

/// <summary>
/// Handles saving and restoring the slide URL list to/from disk.
/// Auto-save location: %APPDATA%\Present\slides.txt
/// </summary>
public static class PersistenceService
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Present");

    private static readonly string DefaultSavePath =
        Path.Combine(AppDataDir, "slides.txt");

    /// <summary>
    /// Load URLs from the auto-save file. Returns empty list if not found.
    /// </summary>
    public static List<string> LoadDefault()
    {
        if (!File.Exists(DefaultSavePath))
            return new List<string>();
        return LoadFrom(DefaultSavePath);
    }

    /// <summary>
    /// Save URLs to the auto-save file.
    /// </summary>
    public static void SaveDefault(IEnumerable<string> urls)
    {
        Directory.CreateDirectory(AppDataDir);
        SaveTo(DefaultSavePath, urls);
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
