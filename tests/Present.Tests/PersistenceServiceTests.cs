using Present.Services;

namespace Present.Tests;

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
}
