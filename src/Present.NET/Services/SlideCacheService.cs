using System.Net.Http;
using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Collections.Concurrent;
using Present.NET.Models;

namespace Present.NET.Services;

public enum SlideCacheKind
{
    Image,
    Page
}

public sealed record SlideCacheEntry(string Url, SlideCacheKind Kind, string FilePath);

public sealed class SlideCacheService
{
    private readonly string _cacheRoot;
    private readonly Func<string, CancellationToken, Task<SlideDownloadResult>> _downloader;
    private readonly ConcurrentDictionary<string, Task<SlideCacheEntry?>> _inFlightDownloads = new();

    public SlideCacheService(string? cacheRoot = null, Func<string, CancellationToken, Task<SlideDownloadResult>>? downloader = null)
    {
        _cacheRoot = cacheRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Present.NET", "cache");

        _downloader = downloader ?? DownloadAsync;

        Directory.CreateDirectory(GetImageCacheDir());
    }

    public async Task<SlideCacheEntry?> EnsureCachedAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || url == "https://")
            return null;

        var kind = SlideHelper.IsImageUrl(url) ? SlideCacheKind.Image : SlideCacheKind.Page;
        if (kind == SlideCacheKind.Page)
        {
            // Web pages are warmed through WebView2 disk cache; we do not write HTML snapshots.
            return new SlideCacheEntry(url, kind, url);
        }

        var cachedPath = TryGetCachedPath(url);
        if (!string.IsNullOrWhiteSpace(cachedPath))
            return new SlideCacheEntry(url, kind, cachedPath);

        var task = _inFlightDownloads.GetOrAdd(url, _ => EnsureImageCachedCoreAsync(url, cancellationToken));
        try
        {
            return await task;
        }
        finally
        {
            _inFlightDownloads.TryRemove(url, out _);
        }
    }

    public string? TryGetCachedPath(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url == "https://")
            return null;

        var kind = SlideHelper.IsImageUrl(url) ? SlideCacheKind.Image : SlideCacheKind.Page;
        if (kind == SlideCacheKind.Page)
            return null;

        var hash = ComputeHash(url);
        var matches = Directory.GetFiles(GetImageCacheDir(), hash + ".*", SearchOption.TopDirectoryOnly);
        return matches.FirstOrDefault();
    }

    public Task RemoveCachedAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url == "https://")
            return Task.CompletedTask;

        var hash = ComputeHash(url);
        var matches = Directory.GetFiles(GetImageCacheDir(), hash + ".*", SearchOption.TopDirectoryOnly);
        foreach (var path in matches)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task ClearAllAsync()
    {
        if (Directory.Exists(_cacheRoot))
            Directory.Delete(_cacheRoot, recursive: true);

        Directory.CreateDirectory(GetImageCacheDir());
        return Task.CompletedTask;
    }

    private async Task<SlideCacheEntry?> EnsureImageCachedCoreAsync(string url, CancellationToken cancellationToken)
    {
        var hash = ComputeHash(url);
        var existingPath = Directory.GetFiles(GetImageCacheDir(), hash + ".*", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(existingPath))
            return new SlideCacheEntry(url, SlideCacheKind.Image, existingPath);

        var download = await _downloader(url, cancellationToken);
        var ext = ResolveImageExtension(url, download);
        if (string.IsNullOrWhiteSpace(ext))
            throw new InvalidDataException($"Downloaded payload is not a recognized image for URL '{url}'.");

        var finalPath = Path.Combine(GetImageCacheDir(), hash + ext);
        var tempPath = Path.Combine(GetImageCacheDir(), hash + ".tmp-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(tempPath, download.Content, cancellationToken);

        foreach (var match in Directory.GetFiles(GetImageCacheDir(), hash + ".*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(match, tempPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(match, finalPath, StringComparison.OrdinalIgnoreCase) && File.Exists(match))
                File.Delete(match);
        }

        if (File.Exists(finalPath))
        {
            File.Delete(tempPath);
        }
        else
        {
            File.Move(tempPath, finalPath);
        }

        return new SlideCacheEntry(url, SlideCacheKind.Image, finalPath);
    }

    private string GetImageCacheDir() => Path.Combine(_cacheRoot, "images");

    private static string? ResolveImageExtension(string originalUrl, SlideDownloadResult download)
    {
        var extFromBytes = TryGetImageExtensionFromMagicBytes(download.Content);
        if (!string.IsNullOrWhiteSpace(extFromBytes))
            return extFromBytes;

        if (!string.IsNullOrWhiteSpace(download.ContentType) &&
            !download.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return null;

        var extFromContentType = download.ContentType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/svg+xml" => ".svg",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(extFromContentType))
            return extFromContentType;

        var extFromFinalUrl = TryGetImageExtensionFromUrl(download.FinalUrl ?? originalUrl);
        if (!string.IsNullOrWhiteSpace(extFromFinalUrl))
            return extFromFinalUrl;

        return null;
    }

    private static string? TryGetImageExtensionFromUrl(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return IsSupportedImageExtension(ext) ? ext : null;
        }
        catch
        {
            var ext = Path.GetExtension(url).ToLowerInvariant();
            return IsSupportedImageExtension(ext) ? ext : null;
        }
    }

    private static string? TryGetImageExtensionFromMagicBytes(byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return ".png";

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ".jpg";

        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return ".webp";

        if (bytes.Length >= 6)
        {
            var sig = Encoding.ASCII.GetString(bytes, 0, 6);
            if (sig is "GIF87a" or "GIF89a")
                return ".gif";
        }

        return null;
    }

    private static bool IsSupportedImageExtension(string ext)
    {
        return ext is ".png" or ".gif" or ".jpg" or ".jpeg" or ".webp" or ".svg";
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<SlideDownloadResult> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Present.NET/1.0 (+https://github.com/charlesroper/present)");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var finalUrl = response.RequestMessage?.RequestUri?.ToString();
        return new SlideDownloadResult(bytes, contentType, finalUrl);
    }

    public sealed record SlideDownloadResult(byte[] Content, string? ContentType, string? FinalUrl = null);
}
