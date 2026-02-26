using Present.NET.Services;

namespace Present.NET.Tests;

public sealed class SlideCacheServiceTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"present-cache-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
            Directory.Delete(_cacheRoot, recursive: true);
    }

    [Fact]
    public async Task EnsureCachedAsync_Image_DownloadsOnce_AndUsesCache()
    {
        var calls = 0;
        var service = new SlideCacheService(
            _cacheRoot,
            (url, _) =>
            {
                calls++;
                return Task.FromResult(new SlideCacheService.SlideDownloadResult([1, 2, 3], "image/jpeg"));
            });

        var first = await service.EnsureCachedAsync("https://example.com/photo.jpg");
        var second = await service.EnsureCachedAsync("https://example.com/photo.jpg");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.FilePath, second!.FilePath);
        Assert.True(File.Exists(first.FilePath));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task EnsureCachedAsync_Page_DoesNotCreateDiskFile()
    {
        var calls = 0;
        var service = new SlideCacheService(
            _cacheRoot,
            (url, _) =>
            {
                calls++;
                return Task.FromResult(new SlideCacheService.SlideDownloadResult([1], "text/html"));
            });

        var entry = await service.EnsureCachedAsync("https://example.com/page");

        Assert.NotNull(entry);
        Assert.Equal(SlideCacheKind.Page, entry!.Kind);
        Assert.Equal("https://example.com/page", entry.FilePath);
        Assert.Equal(0, calls);
        Assert.Null(service.TryGetCachedPath("https://example.com/page"));
    }

    [Fact]
    public async Task RemoveCachedAsync_RemovesSingleSlideCache()
    {
        var service = new SlideCacheService(
            _cacheRoot,
            (url, _) => Task.FromResult(new SlideCacheService.SlideDownloadResult([1, 2], "image/png")));

        var entry = await service.EnsureCachedAsync("https://example.com/a.png");
        Assert.NotNull(entry);
        Assert.True(File.Exists(entry!.FilePath));

        await service.RemoveCachedAsync("https://example.com/a.png");

        Assert.False(File.Exists(entry.FilePath));
    }

    [Fact]
    public async Task ClearAllAsync_RemovesAllCacheFiles()
    {
        var service = new SlideCacheService(
            _cacheRoot,
            (url, _) => Task.FromResult(new SlideCacheService.SlideDownloadResult([1, 2], "image/png")));

        await service.EnsureCachedAsync("https://example.com/a.png");
        await service.EnsureCachedAsync("https://example.com/b.png");

        await service.ClearAllAsync();

        var files = Directory.GetFiles(_cacheRoot, "*", SearchOption.AllDirectories);
        Assert.Empty(files);
    }

    [Fact]
    public async Task TryGetCachedPath_ReturnsNullForUnknownAndPathForCached()
    {
        var service = new SlideCacheService(
            _cacheRoot,
            (url, _) => Task.FromResult(new SlideCacheService.SlideDownloadResult([9], "image/png")));

        Assert.Null(service.TryGetCachedPath("https://example.com/missing.png"));

        var entry = await service.EnsureCachedAsync("https://example.com/found.png");

        Assert.Equal(entry!.FilePath, service.TryGetCachedPath("https://example.com/found.png"));
    }

    [Fact]
    public async Task EnsureCachedAsync_UsesFinalUrlExtension_WhenRedirectedImageChangesExtension()
    {
        var calls = 0;
        var service = new SlideCacheService(
            _cacheRoot,
            (url, _) =>
            {
                calls++;
                return Task.FromResult(new SlideCacheService.SlideDownloadResult(
                    [7, 8, 9],
                    "image/jpeg",
                    "https://fast.example.net/generated/photo.jpg"));
            });

        var entry = await service.EnsureCachedAsync("https://picsum.photos/1200/800.webp");
        var second = await service.EnsureCachedAsync("https://picsum.photos/1200/800.webp");

        Assert.NotNull(entry);
        Assert.EndsWith(".jpg", entry!.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(second);
        Assert.Equal(entry.FilePath, second!.FilePath);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task EnsureCachedAsync_Image_ConcurrentRequests_DownloadOnce()
    {
        var calls = 0;
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var service = new SlideCacheService(
            _cacheRoot,
            async (url, _) =>
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return new SlideCacheService.SlideDownloadResult([1, 2, 3], "image/jpeg", url);
            });

        var firstTask = service.EnsureCachedAsync("https://example.com/concurrent.jpg");
        var secondTask = service.EnsureCachedAsync("https://example.com/concurrent.jpg");

        gate.SetResult(true);
        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, calls);
        Assert.Equal(firstTask.Result!.FilePath, secondTask.Result!.FilePath);
    }

    [Fact]
    public async Task EnsureCachedAsync_Image_UsesMagicBytesExtension_WhenMetadataMisleading()
    {
        var pngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        var service = new SlideCacheService(
            _cacheRoot,
            (url, _) => Task.FromResult(new SlideCacheService.SlideDownloadResult(
                pngHeader,
                "application/octet-stream",
                "https://cdn.example.com/image.bin")));

        var entry = await service.EnsureCachedAsync("https://example.com/misleading.jpg");

        Assert.NotNull(entry);
        Assert.EndsWith(".png", entry!.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureCachedAsync_Image_ThrowsForNonImagePayload()
    {
        var service = new SlideCacheService(
            _cacheRoot,
            (url, _) => Task.FromResult(new SlideCacheService.SlideDownloadResult(
                [0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E],
                "text/html",
                url)));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.EnsureCachedAsync("https://example.com/photo.jpg"));
    }
}
