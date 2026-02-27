using Present.NET.Services;

namespace Present.NET.Tests;

public sealed class PersistenceServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"present-tests-{Guid.NewGuid():N}");

    public PersistenceServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
        PersistenceService.ConfigureStorageRootForTesting(_tempDir);
    }

    public void Dispose()
    {
        PersistenceService.ResetStorageRootForTesting();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void SaveTo_AndLoadFrom_RoundTripUrls()
    {
        var path = Path.Combine(_tempDir, "custom.txt");
        var urls = new[] { "https://example.com", "https://example.com/slide.png" };

        PersistenceService.SaveTo(path, urls);
        var loaded = PersistenceService.LoadFrom(path);

        Assert.Equal(urls, loaded);
    }

    [Fact]
    public void LoadFrom_TrimWhitespace_AndIgnoreBlankLines()
    {
        var path = Path.Combine(_tempDir, "spaces.txt");
        File.WriteAllLines(path, ["  https://example.com  ", "", "   ", "https://foo.bar/page"]);

        var loaded = PersistenceService.LoadFrom(path);

        Assert.Equal(["https://example.com", "https://foo.bar/page"], loaded);
    }

    [Fact]
    public void LoadDefault_ReturnsEmpty_WhenDefaultFileMissing()
    {
        var loaded = PersistenceService.LoadDefault();

        Assert.Empty(loaded);
    }

    [Fact]
    public void SaveDefault_WritesToConfiguredStorageRoot()
    {
        PersistenceService.SaveDefault(["https://example.com/default"]);

        var expectedPath = Path.Combine(_tempDir, "slides.txt");
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(["https://example.com/default"], File.ReadAllLines(expectedPath));
    }

    [Fact]
    public void SaveTo_AndLoadFrom_RoundTripLongAndUnicodeUrls()
    {
        var path = Path.Combine(_tempDir, "unicode.txt");
        var longUrl = "https://example.com/" + new string('a', 256) + "?q=" + new string('z', 128);
        var urls = new[]
        {
            "https://example.com/caf\u00e9",
            "https://example.com/na\u00efve",
            longUrl
        };

        PersistenceService.SaveTo(path, urls);
        var loaded = PersistenceService.LoadFrom(path);

        Assert.Equal(urls, loaded);
    }

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void SaveThemePreference_AndLoadThemePreference_RoundTrip(ThemePreference preference)
    {
        PersistenceService.SaveThemePreference(preference);

        var loaded = PersistenceService.LoadThemePreference();

        Assert.Equal(preference, loaded);
    }

    [Fact]
    public void LoadThemePreference_ReturnsSystem_WhenThemeFileMissing()
    {
        var loaded = PersistenceService.LoadThemePreference();

        Assert.Equal(ThemePreference.System, loaded);
    }

    [Fact]
    public void LoadThemePreference_ReturnsSystem_WhenThemeValueInvalid()
    {
        var path = Path.Combine(_tempDir, "theme.txt");
        File.WriteAllText(path, "sepia");

        var loaded = PersistenceService.LoadThemePreference();

        Assert.Equal(ThemePreference.System, loaded);
    }
}
