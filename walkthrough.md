# Present.NET Linear Code Walkthrough

*2026-02-26T17:39:01Z by Showboat 0.6.1*
<!-- showboat-id: 13b10394-6518-40ed-8f89-9e997a1e7cb7 -->

This walkthrough follows the application in a straight line: project setup, startup, data model, persistence, caching, main window interactions, fullscreen presentation, remote control, and tests. Every section includes commands and output so the explanation is anchored to real source code.

## 1) Project entry points and layout
The app is a .NET 8 WPF desktop application with WebView2 and an embedded ASP.NET Core (Kestrel) remote server.

```bash
ls
```

```output
AGENTS.md
Present.NET.sln
README.md
samples
src
tests
walkthrough.md
```

```bash
sed -n '1,120p' src/Present.NET/Present.NET.csproj
```

```xml
﻿<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <RootNamespace>Present.NET</RootNamespace>
    <AssemblyName>Present.NET</AssemblyName>
    <ApplicationIcon>Assets\present.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <Resource Include="Assets\present.ico" />
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2651.64" />
  </ItemGroup>

</Project>
```

## 2) Application bootstrap
WPF starts at App.xaml, which points directly to MainWindow.xaml as StartupUri.

```bash
sed -n '1,80p' src/Present.NET/App.xaml && sed -n '1,80p' src/Present.NET/App.xaml.cs
```

```text
<Application x:Class="Present.NET.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources/>
</Application>
using System.Windows;

namespace Present.NET;

public partial class App : Application
{
}
```

## 3) Core slide model and URL helper
`SlideItem` is the UI-bound model for each slide URL. It tracks order, cache state, and source (cache vs network).
`SlideHelper` decides whether a URL is an image and can build HTML for image presentation.

```bash
sed -n '1,220p' src/Present.NET/Models/SlideItem.cs
```

```csharp
using System.ComponentModel;

namespace Present.NET.Models;

public enum SlideCacheState
{
    Unknown,
    Caching,
    Cached,
    Failed
}

public enum SlideSource
{
    Unknown,
    Cache,
    Network,
    Failed
}

/// <summary>
/// Represents a single slide entry (a URL) in the presentation.
/// </summary>
public class SlideItem : INotifyPropertyChanged
{
    private string _url = string.Empty;
    private int _number;
    private SlideCacheState _cacheState = SlideCacheState.Unknown;
    private SlideSource _source = SlideSource.Unknown;

    public string Url
    {
        get => _url;
        set
        {
            var normalized = value ?? string.Empty;
            if (_url != normalized)
            {
                _url = normalized;
                OnPropertyChanged(nameof(Url));
            }
        }
    }

    public int Number
    {
        get => _number;
        set
        {
            if (_number != value)
            {
                _number = value;
                OnPropertyChanged(nameof(Number));
            }
        }
    }

    public SlideCacheState CacheState
    {
        get => _cacheState;
        set
        {
            if (_cacheState != value)
            {
                _cacheState = value;
                OnPropertyChanged(nameof(CacheState));
                OnPropertyChanged(nameof(CacheLabel));
                OnPropertyChanged(nameof(CacheSummary));
            }
        }
    }

    public string CacheLabel => CacheState switch
    {
        SlideCacheState.Cached => "Cached",
        SlideCacheState.Caching => "Caching",
        SlideCacheState.Failed => "Failed",
        _ => ""
    };

    public SlideSource Source
    {
        get => _source;
        set
        {
            if (_source != value)
            {
                _source = value;
                OnPropertyChanged(nameof(Source));
                OnPropertyChanged(nameof(SourceLabel));
                OnPropertyChanged(nameof(CacheSummary));
            }
        }
    }

    public string SourceLabel => Source switch
    {
        SlideSource.Cache => "cache",
        SlideSource.Network => "network",
        SlideSource.Failed => "failed",
        _ => ""
    };

    public string CacheSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CacheLabel)) return SourceLabel;
            if (string.IsNullOrWhiteSpace(SourceLabel)) return CacheLabel;
            return $"{CacheLabel} ({SourceLabel})";
        }
    }

    public SlideItem(string url, int number)
    {
        _url = url ?? string.Empty;
        _number = number;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

```bash
sed -n '1,220p' src/Present.NET/Models/SlideHelper.cs
```

```csharp
namespace Present.NET.Models;

/// <summary>
/// Utility methods for slide URL handling.
/// </summary>
public static class SlideHelper
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".gif", ".jpg", ".jpeg", ".webp", ".svg"
    };

    /// <summary>
    /// Returns true if the URL points to an image file.
    /// </summary>
    public static bool IsImageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var dot = path.LastIndexOf('.');
            if (dot < 0) return false;
            return ImageExtensions.Contains(path[dot..]);
        }
        catch
        {
            // Fallback: simple extension check
            var dot = url.LastIndexOf('.');
            if (dot < 0) return false;
            var ext = url[dot..].Split('?', '#')[0];
            return ImageExtensions.Contains(ext);
        }
    }

    /// <summary>
    /// Returns an HTML page that displays the image full-window on a black background.
    /// </summary>
    public static string GetImageHtml(string url)
    {
        var safeUrl = System.Net.WebUtility.HtmlEncode(url);
        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
            * { margin: 0; padding: 0; box-sizing: border-box; }
            html, body {
              width: 100vw; height: 100vh;
              background: #000;
              display: flex; align-items: center; justify-content: center;
              overflow: hidden;
            }
            img {
              max-width: 100%; max-height: 100%;
              object-fit: contain;
              display: block;
            }
            </style>
            </head>
            <body>
              <img src="{{safeUrl}}" alt="Slide"/>
            </body>
            </html>
            """;
    }
}
```

## 4) Persistence and startup data
Slide lists auto-save to `%APPDATA%\Present.NET\slides.txt` and restore at startup. There is legacy fallback support for older `%APPDATA%\Present\slides.txt` installs.

```bash
sed -n '1,220p' src/Present.NET/Services/PersistenceService.cs
```

```csharp
using System.IO;

namespace Present.NET.Services;

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
```

## 5) Caching subsystem
Image slides are cached to disk by URL hash. Page slides are warm-loaded via an offscreen WebView2 instance. The cache service also deduplicates concurrent downloads and validates image payloads.

```bash
sed -n '1,260p' src/Present.NET/Services/SlideCacheService.cs
```

```csharp
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
```

```bash
grep -n "StartCachingAllSlides\|CacheSlideIfNeededAsync\|ResolveSlideUrlAsync\|WarmPageCacheAsync\|ClearCache_Click\|RecacheAll_Click\|ReloadSelectedSlideCache_Click" src/Present.NET/MainWindow.xaml.cs
```

```output
86:        _ = StartCachingAllSlides();
232:        _ = StartCachingAllSlides();
276:        _fullscreenWindow = new FullscreenWindow(_slides.ToList(), startIndex, _zoomFactor, ResolveSlideUrlAsync);
392:                await CacheSlideIfNeededAsync(selected, forceRefresh: false, CancellationToken.None);
394:                _ = CacheSlideIfNeededAsync(selected, forceRefresh: false, CancellationToken.None);
414:        var resolvedUrl = await ResolveSlideUrlAsync(url);
585:    private async void ClearCache_Click(object sender, RoutedEventArgs e)
612:    private async void RecacheAll_Click(object sender, RoutedEventArgs e)
614:        await StartCachingAllSlides(forceRefresh: true);
617:    private async void ReloadSelectedSlideCache_Click(object sender, RoutedEventArgs e)
634:        await CacheSlideIfNeededAsync(slide, forceRefresh: true, CancellationToken.None);
639:    private async Task StartCachingAllSlides(bool forceRefresh = false)
659:                await CacheSlideIfNeededAsync(slide, forceRefresh, token);
676:    private async Task CacheSlideIfNeededAsync(SlideItem slide, bool forceRefresh, CancellationToken cancellationToken)
709:                await WarmPageCacheAsync(slide.Url, cancellationToken);
727:    private async Task<string> ResolveSlideUrlAsync(string originalUrl)
751:    private async Task WarmPageCacheAsync(string url, CancellationToken cancellationToken)
```

```bash
sed -n '560,790p' src/Present.NET/MainWindow.xaml.cs
```

```csharp
    {
        _fullscreenWindow?.Close();
    }

    private async void RemoteScroll(int dy)
    {
        if (_fullscreenWindow != null)
            await _fullscreenWindow.ScrollAsync(dy);
        else if (_previewWebViewReady)
            await PreviewWebView.CoreWebView2.ExecuteScriptAsync($"window.scrollBy(0, {dy});");
    }

    private PresentationStatus GetPresentationStatus()
    {
        return Dispatcher.Invoke(() => new PresentationStatus
        {
            CurrentIndex = _fullscreenWindow?.CurrentIndex ?? SlideListBox.SelectedIndex,
            SlideCount = _slides.Count,
            IsPlaying = _fullscreenWindow != null,
            CurrentUrl = _slides.Count > 0 && SlideListBox.SelectedIndex >= 0
                ? _slides[SlideListBox.SelectedIndex].Url : null,
            ZoomFactor = _zoomFactor
        });
    }

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        await _cacheOperationLock.WaitAsync();
        try
        {
            CacheStatusText.Text = "Clearing cache...";
            await _slideCacheService.ClearAllAsync();
            if (_previewWebViewReady && PreviewWebView.CoreWebView2?.Profile != null)
            {
                await PreviewWebView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
            }

            foreach (var slide in _slides)
                slide.CacheState = SlideCacheState.Unknown;

            CacheStatusText.Text = "Cache cleared";
        }
        catch
        {
            CacheStatusText.Text = "Cache clear failed";
        }
        finally
        {
            _cacheOperationLock.Release();
        }
    }

    private async void RecacheAll_Click(object sender, RoutedEventArgs e)
    {
        await StartCachingAllSlides(forceRefresh: true);
    }

    private async void ReloadSelectedSlideCache_Click(object sender, RoutedEventArgs e)
    {
        if (SlideListBox.SelectedItem is not SlideItem slide)
            return;

        await _cacheOperationLock.WaitAsync();
        try
        {
            CacheStatusText.Text = "Reloading selected slide...";
            await _slideCacheService.RemoveCachedAsync(slide.Url);
            slide.CacheState = SlideCacheState.Unknown;
        }
        finally
        {
            _cacheOperationLock.Release();
        }

        await CacheSlideIfNeededAsync(slide, forceRefresh: true, CancellationToken.None);
        if (SlideListBox.SelectedItem == slide)
            await NavigatePreviewAsync(slide);
    }

    private async Task StartCachingAllSlides(bool forceRefresh = false)
    {
        _cacheCts?.Cancel();
        _cacheCts = new CancellationTokenSource();
        var token = _cacheCts.Token;

        await _cacheOperationLock.WaitAsync(token);
        try
        {
            if (_slides.Count == 0)
            {
                CacheStatusText.Text = "";
                return;
            }

            CacheStatusText.Text = forceRefresh ? "Re-caching all slides..." : "Caching slides...";
            var done = 0;
            foreach (var slide in _slides)
            {
                token.ThrowIfCancellationRequested();
                await CacheSlideIfNeededAsync(slide, forceRefresh, token);
                done++;
                CacheStatusText.Text = $"Caching {done}/{_slides.Count}";
            }

            CacheStatusText.Text = "Cache ready";
        }
        catch (OperationCanceledException)
        {
            CacheStatusText.Text = "";
        }
        finally
        {
            _cacheOperationLock.Release();
        }
    }

    private async Task CacheSlideIfNeededAsync(SlideItem slide, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slide.Url) || slide.Url == "https://")
        {
            slide.CacheState = SlideCacheState.Unknown;
            return;
        }

        try
        {
            slide.CacheState = SlideCacheState.Caching;
            slide.Source = SlideSource.Unknown;

            if (SlideHelper.IsImageUrl(slide.Url))
            {
                if (!forceRefresh && _slideCacheService.TryGetCachedPath(slide.Url) != null)
                {
                    slide.CacheState = SlideCacheState.Cached;
                    slide.Source = SlideSource.Cache;
                    return;
                }

                if (forceRefresh)
                    await _slideCacheService.RemoveCachedAsync(slide.Url);

                await _slideCacheService.EnsureCachedAsync(slide.Url, cancellationToken);
                slide.Source = SlideSource.Cache;
            }
            else
            {
                if (forceRefresh && _previewWebViewReady && PreviewWebView.CoreWebView2?.Profile != null)
                    await PreviewWebView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);

                await WarmPageCacheAsync(slide.Url, cancellationToken);
                slide.Source = SlideSource.Network;
            }

            slide.CacheState = SlideCacheState.Cached;
        }
        catch
        {
            slide.CacheState = SlideCacheState.Failed;
            slide.Source = SlideSource.Failed;
        }
    }

    private static bool IsFileUri(string url)
    {
        return url.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveSlideUrlAsync(string originalUrl)
    {
        if (!SlideHelper.IsImageUrl(originalUrl))
            return originalUrl;

        var cachedPath = _slideCacheService.TryGetCachedPath(originalUrl);
        if (string.IsNullOrWhiteSpace(cachedPath))
        {
            try
            {
                await _slideCacheService.EnsureCachedAsync(originalUrl);
                cachedPath = _slideCacheService.TryGetCachedPath(originalUrl);
            }
            catch
            {
                return originalUrl;
            }
        }

        return string.IsNullOrWhiteSpace(cachedPath)
            ? originalUrl
            : new Uri(cachedPath).AbsoluteUri;
    }

    private async Task WarmPageCacheAsync(string url, CancellationToken cancellationToken)
    {
        if (!_previewWebViewReady || PreloadWebView.CoreWebView2 == null)
            return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, args) =>
        {
            PreloadWebView.CoreWebView2.NavigationCompleted -= handler;
            if (args.IsSuccess)
                tcs.TrySetResult(true);
            else
                tcs.TrySetException(new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}"));
        };

        PreloadWebView.CoreWebView2.NavigationCompleted += handler;
        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        PreloadWebView.CoreWebView2.Navigate(url);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private void RefreshNumbers()
    {
        for (int i = 0; i < _slides.Count; i++)
            _slides[i].Number = i + 1;
    }

    private void MarkDirty()
    {
        _isDirty = true;
        UpdateTitle();
        PersistenceService.SaveDefault(_slides.Select(s => s.Url));
    }

    private void UpdateTitle()
```

```bash
sed -n '40,170p' src/Present.NET/MainWindow.xaml.cs && sed -n '380,460p' src/Present.NET/MainWindow.xaml.cs
```

```csharp
    public MainWindow()
    {
        InitializeComponent();
        SlideListBox.ItemsSource = _slides;
        _slides.CollectionChanged += (_, _) => RefreshNumbers();
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await InitPreviewWebViewAsync();
        LoadDefaultSlides();
        StartRemoteServer();
    }

    private async Task InitPreviewWebViewAsync()
    {
        try
        {
            await PreviewWebView.EnsureCoreWebView2Async();
            await PreloadWebView.EnsureCoreWebView2Async();
            _previewWebViewReady = true;
            PreviewWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            PreviewWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            PreloadWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            PreloadWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 runtime is not installed.\n\n{ex.Message}\n\n" +
                "Download from: https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                "WebView2 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadDefaultSlides()
    {
        var urls = PersistenceService.LoadDefault();
        foreach (var url in urls)
            _slides.Add(new SlideItem(url, _slides.Count + 1));

        RefreshNumbers();
        _isDirty = false;
        UpdateTitle();
        UpdateSlideCountLabel();
        _ = StartCachingAllSlides();
    }

    private void StartRemoteServer()
    {
        _server = new RemoteControlServer(9123);
        _server.OnNext = () => Dispatcher.BeginInvoke(RemoteNext);
        _server.OnPrev = () => Dispatcher.BeginInvoke(RemotePrev);
        _server.OnPlay = () => Dispatcher.BeginInvoke(RemotePlay);
        _server.OnStop = () => Dispatcher.BeginInvoke(RemoteStop);
        _server.OnZoomIn = () => Dispatcher.BeginInvoke(ZoomIn);
        _server.OnZoomOut = () => Dispatcher.BeginInvoke(ZoomOut);
        _server.OnScroll = dy => Dispatcher.BeginInvoke(() => RemoteScroll(dy));
        _server.GetStatus = GetPresentationStatus;

        try
        {
            _server.Start();
            var ip = GetLocalIpAddress();
            _remoteControlUrl = $"http://{ip}:9123/";
            RemoteInfoText.Text = $"Remote: {_remoteControlUrl}";
            CopyRemoteButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _remoteControlUrl = null;
            RemoteInfoText.Text = "Remote control unavailable";
            CopyRemoteButton.IsEnabled = false;
            System.Diagnostics.Debug.WriteLine($"Remote server error: {ex.Message}");
        }
    }

    private void CopyRemoteButton_Click(object sender, RoutedEventArgs e)
    {
        CopyRemoteUrlToClipboard();
    }

    private void RemoteInfoContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            CopyRemoteUrlToClipboard();
            e.Handled = true;
        }
    }

    private void CopyRemoteUrlToClipboard()
    {
        if (string.IsNullOrWhiteSpace(_remoteControlUrl))
            return;

        try
        {
            Clipboard.SetText(_remoteControlUrl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard copy failed: {ex.Message}");
        }
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        return addr.Address.ToString();
                }
            }
        }
        catch { }
        return "localhost";
    }

    // -----------------------------------------------------------------------
    // Window events
    // -----------------------------------------------------------------------
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_previewWebViewReady)
            PreviewWebView.ZoomFactor = _zoomFactor;
    }

    // -----------------------------------------------------------------------
    // Slide list selection to preview
    // -----------------------------------------------------------------------
    private async void SlideListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SlideListBox.SelectedItem is SlideItem selected)
        {
            if (SlideHelper.IsImageUrl(selected.Url))
                await CacheSlideIfNeededAsync(selected, forceRefresh: false, CancellationToken.None);
            else
                _ = CacheSlideIfNeededAsync(selected, forceRefresh: false, CancellationToken.None);

            await NavigatePreviewAsync(selected);
        }
        else
        {
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewWebView.Visibility = Visibility.Collapsed;
        }
    }

    private async Task NavigatePreviewAsync(SlideItem slide)
    {
        var url = slide.Url;
        if (!_previewWebViewReady || string.IsNullOrWhiteSpace(url)) return;
        if (url == "https://") return; // placeholder URL, don't navigate

        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        PreviewWebView.Visibility = Visibility.Visible;

        var resolvedUrl = await ResolveSlideUrlAsync(url);
        var fromCache = IsFileUri(resolvedUrl);
        slide.Source = fromCache ? SlideSource.Cache : SlideSource.Network;

        if (SlideHelper.IsImageUrl(url))
        {
            if (fromCache)
                PreviewWebView.CoreWebView2.Navigate(resolvedUrl);
            else
                PreviewWebView.NavigateToString(SlideHelper.GetImageHtml(resolvedUrl));
        }
        else
            PreviewWebView.CoreWebView2.Navigate(resolvedUrl);

        PreviewWebView.ZoomFactor = _zoomFactor;
    }

    // -----------------------------------------------------------------------
    // TextBox editing in slide list items
    // -----------------------------------------------------------------------
    private void SlideUrlTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Prevent ListBox drag when editing a textbox
        _isDragging = false;
    }

    private void SlideUrlTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is SlideItem item)
        {
            if (item.Url != tb.Text)
            {
                item.Url = tb.Text;
                item.CacheState = SlideCacheState.Unknown;
                MarkDirty();
                // Refresh preview if this is the selected item
                if (SlideListBox.SelectedItem == item)
                    _ = NavigatePreviewAsync(item);
            }
        }
        PersistenceService.SaveDefault(_slides.Select(s => s.Url));
    }

    private void SlideUrlTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
```

## 6) Main window UI structure
The toolbar exposes presentation controls, cache controls, and remote URL copy actions. The left pane is an editable URL list with per-slide cache/source status. The right pane has a visible preview WebView and a hidden preload WebView.

```bash
sed -n '1,240p' src/Present.NET/MainWindow.xaml
```

```xaml
<Window x:Class="Present.NET.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
        Title="Present.NET" Height="640" Width="1024" MinWidth="600" MinHeight="400"
        Icon="Assets/present.ico"
        Closing="Window_Closing">
    <DockPanel>
        <!-- Menu -->
        <Menu DockPanel.Dock="Top">
            <MenuItem Header="_File">
                <MenuItem Header="_Open..." Click="OpenFile_Click" InputGestureText="Ctrl+O"/>
                <MenuItem Header="_Save" Click="SaveFile_Click" InputGestureText="Ctrl+S"/>
                <MenuItem Header="Save _As..." Click="SaveFileAs_Click" InputGestureText="Ctrl+Shift+S"/>
                <Separator/>
                <MenuItem Header="_Exit" Click="Exit_Click"/>
            </MenuItem>
            <MenuItem Header="_Presentation">
                <MenuItem Header="_Play" Click="Play_Click" InputGestureText="F5"/>
            </MenuItem>
            <MenuItem Header="_View">
                <MenuItem Header="Zoom _In" Click="ZoomIn_Click" InputGestureText="Ctrl+="/>
                <MenuItem Header="Zoom _Out" Click="ZoomOut_Click" InputGestureText="Ctrl+-"/>
                <MenuItem Header="_Reset Zoom" Click="ZoomReset_Click" InputGestureText="Ctrl+0"/>
            </MenuItem>
            <MenuItem Header="_Help">
                <MenuItem Header="_Remote Control" Click="ShowRemoteInfo_Click"/>
            </MenuItem>
        </Menu>

        <!-- Toolbar -->
        <ToolBar DockPanel.Dock="Top" Background="#F3F3F3">
            <Button Click="Play_Click" ToolTip="Play Presentation (F5)" Padding="8,4">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="Play"/>
                </StackPanel>
            </Button>
            <Separator/>
            <Button Click="AddSlide_Click" ToolTip="Add Slide" Padding="8,4" Content="+ Add Slide"/>
            <Button Click="RemoveSlide_Click" ToolTip="Remove Selected Slide" Padding="8,4" Content="Remove"/>
            <Separator/>
            <Button Click="MoveUp_Click" ToolTip="Move Slide Up" Padding="8,4" Content="Up"/>
            <Button Click="MoveDown_Click" ToolTip="Move Slide Down" Padding="8,4" Content="Down"/>
            <Separator/>
            <Button Click="ZoomIn_Click" ToolTip="Zoom In (Ctrl+=)" Padding="8,4" Content="Zoom +"/>
            <Button Click="ZoomOut_Click" ToolTip="Zoom Out (Ctrl+-)" Padding="8,4" Content="Zoom -"/>
            <Button Click="ZoomReset_Click" ToolTip="Reset Zoom (Ctrl+0)" Padding="8,4" Content="Reset"/>
            <Separator/>
            <Button x:Name="ReloadSlideCacheButton" Click="ReloadSelectedSlideCache_Click" ToolTip="Purge and re-cache selected slide" Padding="8,4" Content="Reload Slide"/>
            <Button x:Name="RecacheAllButton" Click="RecacheAll_Click" ToolTip="Purge and re-cache all slides" Padding="8,4" Content="Re-cache"/>
            <Button x:Name="ClearCacheButton" Click="ClearCache_Click" ToolTip="Clear all cached slide content" Padding="8,4" Content="Clear Cache"/>
            <TextBlock x:Name="CacheStatusText" VerticalAlignment="Center" Foreground="#555" FontSize="11" Margin="6,0,0,0"/>
            <Separator/>
            <Border x:Name="RemoteInfoContainer"
                    Padding="4,2"
                    Margin="0"
                    CornerRadius="3"
                    Background="Transparent"
                    MouseLeftButtonDown="RemoteInfoContainer_MouseLeftButtonDown"
                    ToolTip="Double-click to copy remote URL">
                <TextBlock x:Name="RemoteInfoText" VerticalAlignment="Center" Foreground="#555"
                           FontSize="11"/>
            </Border>
            <Button x:Name="CopyRemoteButton"
                    Click="CopyRemoteButton_Click"
                    ToolTip="Copy remote URL"
                    Padding="6,2"
                    Margin="4,0,0,0"
                    VerticalAlignment="Center"
                    IsEnabled="False"
                    FontFamily="Segoe MDL2 Assets"
                    Content="&#xE8C8;"/>
        </ToolBar>

        <!-- Main content area -->
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="260" MinWidth="150" MaxWidth="500"/>
                <ColumnDefinition Width="5"/>
                <ColumnDefinition Width="*" MinWidth="200"/>
            </Grid.ColumnDefinitions>

            <!-- Left sidebar: slide list -->
            <DockPanel Grid.Column="0" Background="#FAFAFA">
                <Border DockPanel.Dock="Top" Background="#E8E8E8" BorderBrush="#D0D0D0"
                        BorderThickness="0,0,0,1" Padding="8,4">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Slides" FontWeight="SemiBold" VerticalAlignment="Center"/>
                        <TextBlock x:Name="SlideCountLabel" Foreground="#666" Margin="6,0,0,0"
                                   VerticalAlignment="Center" FontSize="11"/>
                    </StackPanel>
                </Border>
                <ListBox x:Name="SlideListBox"
                         AllowDrop="True"
                         SelectionChanged="SlideListBox_SelectionChanged"
                         PreviewMouseLeftButtonDown="SlideListBox_PreviewMouseLeftButtonDown"
                         PreviewMouseMove="SlideListBox_PreviewMouseMove"
                         Drop="SlideListBox_Drop"
                         DragOver="SlideListBox_DragOver"
                         ScrollViewer.VerticalScrollBarVisibility="Auto"
                         BorderThickness="0"
                         Background="Transparent"
                         VirtualizingPanel.IsVirtualizing="False">
                    <ListBox.ItemContainerStyle>
                        <Style TargetType="ListBoxItem">
                            <Setter Property="Padding" Value="0"/>
                            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
                            <Setter Property="Template">
                                <Setter.Value>
                                    <ControlTemplate TargetType="ListBoxItem">
                                        <Border x:Name="ItemBorder"
                                                Background="{TemplateBinding Background}"
                                                BorderThickness="0,0,0,1"
                                                BorderBrush="#E8E8E8">
                                            <ContentPresenter Margin="0"/>
                                        </Border>
                                        <ControlTemplate.Triggers>
                                            <Trigger Property="IsSelected" Value="True">
                                                <Setter TargetName="ItemBorder" Property="Background" Value="#0078D4"/>
                                                <Setter Property="Foreground" Value="White"/>
                                            </Trigger>
                                            <Trigger Property="IsMouseOver" Value="True">
                                                <Setter TargetName="ItemBorder" Property="Background" Value="#E5F3FB"/>
                                            </Trigger>
                                            <MultiTrigger>
                                                <MultiTrigger.Conditions>
                                                    <Condition Property="IsSelected" Value="True"/>
                                                    <Condition Property="IsMouseOver" Value="True"/>
                                                </MultiTrigger.Conditions>
                                                <Setter TargetName="ItemBorder" Property="Background" Value="#0067BE"/>
                                            </MultiTrigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </Setter.Value>
                            </Setter>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="0" Background="Transparent">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="32"/>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="100"/>
                                </Grid.ColumnDefinitions>
                                <TextBlock Grid.Column="0"
                                           Text="{Binding Number}"
                                           Foreground="#888"
                                           FontSize="11"
                                           HorizontalAlignment="Center"
                                           VerticalAlignment="Center"
                                           Margin="0,8"/>
                                <TextBox Grid.Column="1"
                                         Text="{Binding Url, UpdateSourceTrigger=LostFocus}"
                                         BorderThickness="0"
                                         Background="Transparent"
                                         Padding="4,8,8,8"
                                         FontSize="12"
                                         TextWrapping="NoWrap"
                                         AcceptsReturn="False"
                                         GotFocus="SlideUrlTextBox_GotFocus"
                                         LostFocus="SlideUrlTextBox_LostFocus"
                                         PreviewKeyDown="SlideUrlTextBox_PreviewKeyDown"
                                         ToolTip="{Binding Url}">
                                    <TextBox.Style>
                                        <Style TargetType="TextBox">
                                            <Setter Property="Foreground" Value="#333"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=IsSelected}" Value="True">
                                                    <Setter Property="Foreground" Value="White"/>
                                                    <Setter Property="CaretBrush" Value="White"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBox.Style>
                                </TextBox>
                                <TextBlock Grid.Column="2"
                                           Text="{Binding CacheSummary}"
                                           Foreground="#888"
                                           FontSize="10"
                                           HorizontalAlignment="Right"
                                           VerticalAlignment="Center"
                                           Margin="0,0,8,0"/>
                            </Grid>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </DockPanel>

            <!-- Splitter -->
            <GridSplitter Grid.Column="1" Width="5" HorizontalAlignment="Stretch"
                          Background="#D0D0D0" Cursor="SizeWE"/>

            <!-- Right: preview pane -->
            <Grid Grid.Column="2" Background="#222">
                <wv2:WebView2 x:Name="PreviewWebView"
                              Visibility="Collapsed"/>
                <wv2:WebView2 x:Name="PreloadWebView"
                              Visibility="Collapsed"
                              IsHitTestVisible="False"/>
                <Border x:Name="PreviewPlaceholder"
                        Background="#222"
                        HorizontalAlignment="Center"
                        VerticalAlignment="Center">
                    <StackPanel HorizontalAlignment="Center">
                        <TextBlock Text="Preview" FontSize="24" HorizontalAlignment="Center"
                                   Foreground="#555" Margin="0,0,0,12"/>
                        <TextBlock Text="Add slides in the sidebar" Foreground="#666"
                                   FontSize="14" HorizontalAlignment="Center"/>
                        <TextBlock Text="then select one to preview" Foreground="#555"
                                   FontSize="12" HorizontalAlignment="Center" Margin="0,4,0,0"/>
                    </StackPanel>
                </Border>
            </Grid>
        </Grid>
    </DockPanel>
</Window>
```

## 7) Fullscreen presentation window
The fullscreen window receives the slide list, keeps its own index/zoom state, and asks the async URL resolver for each navigation so cached image file URLs can be used there too.

```bash
sed -n '1,240p' src/Present.NET/FullscreenWindow.xaml.cs
```

```csharp
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Present.NET.Models;

namespace Present.NET;

/// <summary>
/// Fullscreen presentation window.
/// Arrow keys navigate slides; Escape exits; +/- zoom.
/// </summary>
public partial class FullscreenWindow : Window
{
    private readonly List<SlideItem> _slides;
    private readonly Func<string, Task<string>>? _urlResolverAsync;
    private readonly SemaphoreSlim _navigationLock = new(1, 1);
    private int _currentIndex;
    private double _zoomFactor;
    private bool _webViewReady;

    public int CurrentIndex => _currentIndex;
    public double ZoomFactor => _zoomFactor;

    public FullscreenWindow(List<SlideItem> slides, int startIndex, double zoomFactor, Func<string, Task<string>>? urlResolverAsync = null)
    {
        InitializeComponent();
        _slides = slides;
        _urlResolverAsync = urlResolverAsync;
        _currentIndex = Math.Clamp(startIndex, 0, Math.Max(0, slides.Count - 1));
        _zoomFactor = zoomFactor;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await SlideWebView.EnsureCoreWebView2Async();
        _webViewReady = true;
        SlideWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        SlideWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        await NavigateToCurrentSlideAsync();
        UpdateCounter();
    }

    private async Task NavigateToCurrentSlideAsync()
    {
        if (!_webViewReady || _slides.Count == 0) return;

        await _navigationLock.WaitAsync();
        try
        {
            var url = _slides[_currentIndex].Url;
            var resolvedUrl = _urlResolverAsync != null
                ? await _urlResolverAsync(url)
                : url;

            if (SlideHelper.IsImageUrl(url))
            {
                if (resolvedUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    SlideWebView.CoreWebView2.Navigate(resolvedUrl);
                else
                    SlideWebView.NavigateToString(SlideHelper.GetImageHtml(resolvedUrl));
            }
            else
            {
                SlideWebView.CoreWebView2.Navigate(resolvedUrl);
            }
            SlideWebView.ZoomFactor = _zoomFactor;
            UpdateCounter();
        }
        finally
        {
            _navigationLock.Release();
        }
    }

    private void UpdateCounter()
    {
        SlideCounterText.Text = _slides.Count > 0
            ? $"{_currentIndex + 1} / {_slides.Count}"
            : "0 / 0";
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;

            case Key.Right:
            case Key.Down:
            case Key.Space:
            case Key.PageDown:
                NavigateNext();
                break;

            case Key.Left:
            case Key.Up:
            case Key.PageUp:
                NavigatePrev();
                break;

            case Key.OemPlus:
            case Key.Add:
                if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
                    ZoomIn();
                break;

            case Key.OemMinus:
            case Key.Subtract:
                if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
                    ZoomOut();
                break;

            case Key.D0:
            case Key.NumPad0:
                if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
                    ZoomReset();
                break;

            case Key.F:
                // Hide/show counter
                CounterBorder.Visibility = CounterBorder.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                break;
        }
        e.Handled = true;
    }

    public void NavigateNext()
    {
        if (_slides.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % _slides.Count;
        _ = NavigateToCurrentSlideAsync();
    }

    public void NavigatePrev()
    {
        if (_slides.Count == 0) return;
        _currentIndex = (_currentIndex - 1 + _slides.Count) % _slides.Count;
        _ = NavigateToCurrentSlideAsync();
    }

    public void ZoomIn()
    {
        _zoomFactor = Math.Min(_zoomFactor * 1.1, 5.0);
        if (_webViewReady) SlideWebView.ZoomFactor = _zoomFactor;
    }

    public void ZoomOut()
    {
        _zoomFactor = Math.Max(_zoomFactor / 1.1, 0.1);
        if (_webViewReady) SlideWebView.ZoomFactor = _zoomFactor;
    }

    public void ZoomReset()
    {
        _zoomFactor = 1.0;
        if (_webViewReady) SlideWebView.ZoomFactor = _zoomFactor;
    }

    public async Task ScrollAsync(int dy)
    {
        if (!_webViewReady) return;
        await SlideWebView.CoreWebView2.ExecuteScriptAsync($"window.scrollBy(0, {dy});");
    }
}
```

## 8) Remote control server (Kestrel on :9123)
MainWindow registers delegates for next/prev/play/stop/zoom/scroll. The server exposes HTTP endpoints and returns JSON status for remote UIs. The `/` endpoint serves an inlined mobile control page.

```bash
sed -n '1,220p' src/Present.NET/Services/RemoteControlServer.cs
```

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Present.NET.Services;

/// <summary>
/// Embedded HTTP server on port 9123 providing remote control of the presentation.
/// Endpoints: GET /next, /prev, /play, /stop, /zoomin, /zoomout, /scroll?dy=N, /status, /
/// </summary>
public class RemoteControlServer : IDisposable
{
    private readonly int _port;
    private CancellationTokenSource? _cts;
    private WebApplication? _app;
    private bool _disposed;
    private bool _started;
    private readonly object _lifecycleLock = new();

    // Actions dispatched to the UI
    public Action? OnNext { get; set; }
    public Action? OnPrev { get; set; }
    public Action? OnPlay { get; set; }
    public Action? OnStop { get; set; }
    public Action? OnZoomIn { get; set; }
    public Action? OnZoomOut { get; set; }
    public Action<int>? OnScroll { get; set; }

    // Status provider
    public Func<PresentationStatus>? GetStatus { get; set; }

    public RemoteControlServer(int port = 9123)
    {
        _port = port;
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RemoteControlServer));
            if (_started) return;

            _cts = new CancellationTokenSource();
            _app = CreateWebApp();
            _app.StartAsync(_cts.Token).GetAwaiter().GetResult();
            _started = true;
        }
    }

    public void Stop()
    {
        WebApplication? appToStop;
        CancellationTokenSource? ctsToCancel;

        lock (_lifecycleLock)
        {
            if (!_started) return;
            _started = false;

            appToStop = _app;
            _app = null;

            ctsToCancel = _cts;
            _cts = null;
        }

        try { ctsToCancel?.Cancel(); } catch { }

        if (appToStop != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await appToStop.StopAsync();
                }
                catch { }
                finally
                {
                    try { await appToStop.DisposeAsync(); } catch { }
                }
            });
        }

        ctsToCancel?.Dispose();
    }

    private WebApplication CreateWebApp()
    {
        var options = new WebApplicationOptions { Args = [] };
        var builder = WebApplication.CreateSlimBuilder(options);
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(server =>
        {
            server.ListenAnyIP(_port);
        });

        var app = builder.Build();

        app.MapGet("/", () => Results.Content(GetControlPageHtml(), "text/html; charset=utf-8"));
        app.MapGet("/next", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnNext));
        app.MapGet("/prev", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnPrev));
        app.MapGet("/play", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnPlay));
        app.MapGet("/stop", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnStop));
        app.MapGet("/zoomin", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnZoomIn));
        app.MapGet("/zoomout", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnZoomOut));
        app.MapGet("/scroll", (HttpContext ctx) =>
        {
            if (TryGetIntQueryParam(ctx.Request, "dy", out var dy))
                OnScroll?.Invoke(dy);

            return BuildStatusResult(ctx);
        });
        app.MapGet("/status", (HttpContext ctx) => BuildStatusResult(ctx));

        return app;
    }

    private IResult ExecuteAndReturnStatus(HttpContext ctx, Action? callback)
    {
        callback?.Invoke();
        return BuildStatusResult(ctx);
    }

    private IResult BuildStatusResult(HttpContext ctx)
    {
        ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
        var status = GetStatus?.Invoke() ?? new PresentationStatus();
        return Results.Json(status);
    }

    private static bool TryGetIntQueryParam(HttpRequest request, string param, out int value)
    {
        value = 0;
        return int.TryParse(request.Query[param], out value);
    }

    private static string GetControlPageHtml() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">
          <title>Present.NET Remote</title>
          <style>
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body {
              background: #111;
              color: #fff;
              font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
              min-height: 100vh;
              display: flex;
              flex-direction: column;
              align-items: center;
              justify-content: center;
              padding: 20px;
            }
            h1 { font-size: 1.4em; margin-bottom: 8px; }
            #slide-info {
              font-size: 1.1em;
              color: #aaa;
              margin-bottom: 20px;
              min-height: 1.4em;
            }
            #slide-info span { color: #0af; font-weight: bold; }
            .grid {
              display: grid;
              grid-template-columns: 1fr 1fr;
              gap: 12px;
              width: 100%;
              max-width: 360px;
            }
            button {
              background: #2a2a2a;
              color: #fff;
              border: 1px solid #444;
              border-radius: 14px;
              padding: 22px 12px;
              font-size: 1.1em;
              cursor: pointer;
              touch-action: manipulation;
              -webkit-tap-highlight-color: transparent;
              transition: background 0.1s;
            }
            button:active { background: #444; }
            button.wide { grid-column: span 2; }
            button.play { background: #0a5; border-color: #0c6; }
            button.play:active { background: #0c6; }
            button.stop { background: #733; border-color: #944; }
            button.stop:active { background: #955; }
            .scroll-row {
              grid-column: span 2;
              display: grid;
              grid-template-columns: 1fr 1fr;
              gap: 12px;
            }
            #status-bar {
              margin-top: 20px;
              font-size: 0.8em;
              color: #555;
            }
            #status-bar.connected { color: #0a5; }
            #status-bar.error { color: #c44; }
          </style>
        </head>
        <body>
          <h1>Present.NET Remote</h1>
          <div id="slide-info">Connecting...</div>
          <div class="grid">
            <button onclick="cmd('prev')">Prev</button>
            <button onclick="cmd('next')">Next</button>
            <button class="wide play" onclick="cmd('play')">Play Fullscreen</button>
            <button class="wide stop" onclick="cmd('stop')">Stop</button>
            <button onclick="cmd('zoomin')">Zoom In</button>
            <button onclick="cmd('zoomout')">Zoom Out</button>
            <button onclick="cmd('scroll?dy=-200')">Scroll Up</button>
            <button onclick="cmd('scroll?dy=200')">Scroll Down</button>
          </div>
```

## 9) Editing workflow and file operations
MainWindow owns the slide collection, file open/save, add/remove/reorder operations, and keyboard shortcuts. This is where persistence is updated and title dirty-state is maintained.

```bash
grep -n "OpenFile_Click\|SaveFile_Click\|SaveFileAs_Click\|AddSlide_Click\|RemoveSlide_Click\|MoveUp_Click\|MoveDown_Click\|MarkDirty\|UpdateTitle" src/Present.NET/MainWindow.xaml.cs
```

```output
84:        UpdateTitle();
194:        else if (e.Key == Key.O && e.KeyboardDevice.Modifiers == ModifierKeys.Control) { OpenFile_Click(this, new RoutedEventArgs()); e.Handled = true; }
195:        else if (e.Key == Key.S && e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { SaveFileAs_Click(this, new RoutedEventArgs()); e.Handled = true; }
196:        else if (e.Key == Key.S && e.KeyboardDevice.Modifiers == ModifierKeys.Control) { SaveFile_Click(this, new RoutedEventArgs()); e.Handled = true; }
205:    private void OpenFile_Click(object sender, RoutedEventArgs e)
230:        UpdateTitle();
235:    private void SaveFile_Click(object sender, RoutedEventArgs e) => SaveCurrentFile();
237:    private void SaveFileAs_Click(object sender, RoutedEventArgs e)
250:        UpdateTitle();
255:        if (_currentFilePath == null) { SaveFileAs_Click(this, new RoutedEventArgs()); return; }
258:        UpdateTitle();
293:    private void AddSlide_Click(object sender, RoutedEventArgs e)
299:        MarkDirty();
317:    private void RemoveSlide_Click(object sender, RoutedEventArgs e)
324:            MarkDirty();
332:    private void MoveUp_Click(object sender, RoutedEventArgs e)
338:        MarkDirty();
341:    private void MoveDown_Click(object sender, RoutedEventArgs e)
347:        MarkDirty();
448:                MarkDirty();
523:            MarkDirty();
783:    private void MarkDirty()
786:        UpdateTitle();
790:    private void UpdateTitle()
```

```bash
sed -n '205,360p' src/Present.NET/MainWindow.xaml.cs && sed -n '780,819p' src/Present.NET/MainWindow.xaml.cs
```

```csharp
    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty)
        {
            var r = MessageBox.Show("Save current file first?", "Present.NET",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) SaveCurrentFile();
        }

        var dlg = new OpenFileDialog
        {
            Title = "Open Slide List",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".txt"
        };
        if (dlg.ShowDialog() != true) return;

        var urls = PersistenceService.LoadFrom(dlg.FileName);
        _slides.Clear();
        foreach (var url in urls)
            _slides.Add(new SlideItem(url, _slides.Count + 1));
        _currentFilePath = dlg.FileName;
        _isDirty = false;
        RefreshNumbers();
        UpdateTitle();
        UpdateSlideCountLabel();
        _ = StartCachingAllSlides();
    }

    private void SaveFile_Click(object sender, RoutedEventArgs e) => SaveCurrentFile();

    private void SaveFileAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save Slide List As",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".txt",
            FileName = _currentFilePath ?? "slides.txt"
        };
        if (dlg.ShowDialog() != true) return;
        _currentFilePath = dlg.FileName;
        PersistenceService.SaveTo(_currentFilePath, _slides.Select(s => s.Url));
        _isDirty = false;
        UpdateTitle();
    }

    private void SaveCurrentFile()
    {
        if (_currentFilePath == null) { SaveFileAs_Click(this, new RoutedEventArgs()); return; }
        PersistenceService.SaveTo(_currentFilePath, _slides.Select(s => s.Url));
        _isDirty = false;
        UpdateTitle();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // -----------------------------------------------------------------------
    // Presentation menu / toolbar
    // -----------------------------------------------------------------------
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_slides.Count == 0)
        {
            MessageBox.Show("No slides to present. Add some URLs first.", "Present.NET",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var startIndex = SlideListBox.SelectedIndex >= 0 ? SlideListBox.SelectedIndex : 0;
        _fullscreenWindow = new FullscreenWindow(_slides.ToList(), startIndex, _zoomFactor, ResolveSlideUrlAsync);
        _fullscreenWindow.Closed += (_, _) => _fullscreenWindow = null;
        _fullscreenWindow.Show();
    }

    private void ShowRemoteInfo_Click(object sender, RoutedEventArgs e)
    {
        var ip = GetLocalIpAddress();
        MessageBox.Show(
            $"Open a browser on your phone and navigate to:\n\nhttp://{ip}:9123/\n\n" +
            "Use the remote control page to navigate slides.",
            "Remote Control", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // -----------------------------------------------------------------------
    // Slide list toolbar buttons
    // -----------------------------------------------------------------------
    private void AddSlide_Click(object sender, RoutedEventArgs e)
    {
        var item = new SlideItem("https://", _slides.Count + 1);
        _slides.Add(item);
        SlideListBox.SelectedItem = item;
        SlideListBox.ScrollIntoView(item);
        MarkDirty();
        UpdateSlideCountLabel();

        // Focus the textbox in the new item
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (SlideListBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem lbi)
            {
                var tb = FindVisualChild<TextBox>(lbi);
                if (tb != null)
                {
                    tb.Focus();
                    tb.SelectAll();
                }
            }
        });
    }

    private void RemoveSlide_Click(object sender, RoutedEventArgs e)
    {
        if (SlideListBox.SelectedItem is SlideItem selected)
        {
            var idx = _slides.IndexOf(selected);
            _slides.Remove(selected);
            RefreshNumbers();
            MarkDirty();
            UpdateSlideCountLabel();
            // Select nearest item
            if (_slides.Count > 0)
                SlideListBox.SelectedIndex = Math.Min(idx, _slides.Count - 1);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var idx = SlideListBox.SelectedIndex;
        if (idx <= 0) return;
        _slides.Move(idx, idx - 1);
        SlideListBox.SelectedIndex = idx - 1;
        MarkDirty();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var idx = SlideListBox.SelectedIndex;
        if (idx < 0 || idx >= _slides.Count - 1) return;
        _slides.Move(idx, idx + 1);
        SlideListBox.SelectedIndex = idx + 1;
        MarkDirty();
    }

    // -----------------------------------------------------------------------
    // Zoom
    // -----------------------------------------------------------------------
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => ZoomReset();

    private void ZoomIn()
    {
        _zoomFactor = Math.Min(_zoomFactor * 1.1, 5.0);
        ApplyZoom();
            _slides[i].Number = i + 1;
    }

    private void MarkDirty()
    {
        _isDirty = true;
        UpdateTitle();
        PersistenceService.SaveDefault(_slides.Select(s => s.Url));
    }

    private void UpdateTitle()
    {
        var dirty = _isDirty ? "* " : "";
        var file = _currentFilePath != null
            ? System.IO.Path.GetFileName(_currentFilePath)
            : "Untitled";
        Title = $"{dirty}{file} - Present.NET";
    }

    private void UpdateSlideCountLabel()
    {
        SlideCountLabel.Text = _slides.Count == 1 ? "(1 slide)" : $"({_slides.Count} slides)";
    }

    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T t) return t;
            obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
```

```bash
sed -n '350,383p' src/Present.NET/MainWindow.xaml.cs
```

```csharp
    // -----------------------------------------------------------------------
    // Zoom
    // -----------------------------------------------------------------------
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => ZoomReset();

    private void ZoomIn()
    {
        _zoomFactor = Math.Min(_zoomFactor * 1.1, 5.0);
        ApplyZoom();
        _fullscreenWindow?.ZoomIn();
    }

    private void ZoomOut()
    {
        _zoomFactor = Math.Max(_zoomFactor / 1.1, 0.1);
        ApplyZoom();
        _fullscreenWindow?.ZoomOut();
    }

    private void ZoomReset()
    {
        _zoomFactor = 1.0;
        ApplyZoom();
        _fullscreenWindow?.ZoomReset();
    }

    private void ApplyZoom()
    {
        if (_previewWebViewReady)
            PreviewWebView.ZoomFactor = _zoomFactor;
    }

```

```bash
sed -n '773,798p' src/Present.NET/MainWindow.xaml.cs
```

```csharp

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private void RefreshNumbers()
    {
        for (int i = 0; i < _slides.Count; i++)
            _slides[i].Number = i + 1;
    }

    private void MarkDirty()
    {
        _isDirty = true;
        UpdateTitle();
        PersistenceService.SaveDefault(_slides.Select(s => s.Url));
    }

    private void UpdateTitle()
    {
        var dirty = _isDirty ? "* " : "";
        var file = _currentFilePath != null
            ? System.IO.Path.GetFileName(_currentFilePath)
            : "Untitled";
        Title = $"{dirty}{file} - Present.NET";
    }

```

## 10) Tests and quality gates
The test suite covers helpers, model notifications, persistence, remote server behavior, and cache service edge cases. UI smoke tests are gated separately.

```bash
ls tests && ls tests/Present.NET.Tests && ls tests/Present.NET.UiTests
```

```output
Present.NET.Tests
Present.NET.UiTests
PersistenceServiceTests.cs
Present.NET.Tests.csproj
RemoteControlServerTests.cs
SlideCacheServiceTests.cs
SlideHelperTests.cs
SlideItemTests.cs
bin
obj
Present.NET.UiTests.csproj
UiSmokeTests.cs
bin
obj
```

```bash
sed -n '1,240p' tests/Present.NET.Tests/SlideCacheServiceTests.cs
```

```csharp
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
```

```bash
sed -n '1,220p' tests/Present.NET.Tests/RemoteControlServerTests.cs
```

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Diagnostics;
using Present.NET.Services;

namespace Present.NET.Tests;

public sealed class RemoteControlServerTests : IDisposable
{
    private readonly List<RemoteControlServer> _servers = [];
    private readonly HttpClient _http = new();

    [Fact]
    public async Task Status_ReturnsJsonPayload()
    {
        using var testServer = StartServer(_ => { }, statusFactory: () => new PresentationStatus
        {
            CurrentIndex = 2,
            SlideCount = 5,
            IsPlaying = true,
            CurrentUrl = "https://example.com",
            ZoomFactor = 1.5
        });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}/status");
        var payload = await response.Content.ReadFromJsonAsync<PresentationStatus>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.CurrentIndex);
        Assert.Equal(5, payload.SlideCount);
        Assert.True(payload.IsPlaying);
    }

    [Theory]
    [InlineData("/next")]
    [InlineData("/prev")]
    [InlineData("/play")]
    [InlineData("/stop")]
    [InlineData("/zoomin")]
    [InlineData("/zoomout")]
    public async Task Command_Endpoint_InvokesExpectedCallback(string path)
    {
        var next = 0;
        var prev = 0;
        var play = 0;
        var stop = 0;
        var zoomIn = 0;
        var zoomOut = 0;

        using var testServer = StartServer(s =>
        {
            s.OnNext = () => Interlocked.Increment(ref next);
            s.OnPrev = () => Interlocked.Increment(ref prev);
            s.OnPlay = () => Interlocked.Increment(ref play);
            s.OnStop = () => Interlocked.Increment(ref stop);
            s.OnZoomIn = () => Interlocked.Increment(ref zoomIn);
            s.OnZoomOut = () => Interlocked.Increment(ref zoomOut);
        });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}{path}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(path == "/next" ? 1 : 0, Volatile.Read(ref next));
        Assert.Equal(path == "/prev" ? 1 : 0, Volatile.Read(ref prev));
        Assert.Equal(path == "/play" ? 1 : 0, Volatile.Read(ref play));
        Assert.Equal(path == "/stop" ? 1 : 0, Volatile.Read(ref stop));
        Assert.Equal(path == "/zoomin" ? 1 : 0, Volatile.Read(ref zoomIn));
        Assert.Equal(path == "/zoomout" ? 1 : 0, Volatile.Read(ref zoomOut));
    }

    [Fact]
    public async Task Unknown_Endpoint_ReturnsNotFound()
    {
        using var testServer = StartServer(_ => { });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Scroll_WithoutDy_DoesNotInvokeCallback()
    {
        var called = 0;
        using var testServer = StartServer(s =>
        {
            s.OnScroll = _ => Interlocked.Increment(ref called);
        });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}/scroll");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, Volatile.Read(ref called));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(-200)]
    public async Task Scroll_WithDy_InvokesCallback(int dy)
    {
        var observed = int.MinValue;
        using var testServer = StartServer(s =>
        {
            s.OnScroll = value => observed = value;
        });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}/scroll?dy={dy}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(dy, observed);
    }

    [Fact]
    public async Task Status_Response_IncludesCorsHeader()
    {
        using var testServer = StartServer(_ => { });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Contains("*", values!);
    }

    [Fact]
    public void Stop_And_Dispose_AreIdempotent()
    {
        using var testServer = StartServer(_ => { });
        var server = testServer.Server;

        server.Stop();
        server.Stop();
        server.Dispose();
        server.Dispose();
    }

    [Fact]
    public void Start_AfterDispose_ThrowsObjectDisposedException()
    {
        var server = new RemoteControlServer(GetFreePort());

        server.Dispose();

        Assert.Throws<ObjectDisposedException>(() => server.Start());
    }

    [Fact]
    public async Task Stop_ReturnsPromptly_DuringSlowInFlightRequest()
    {
        using var testServer = StartServer(s =>
        {
            s.OnNext = () => Thread.Sleep(1500);
        });

        var request = _http.GetAsync($"http://localhost:{testServer.Port}/next");
        await Task.Delay(100);

        var sw = Stopwatch.StartNew();
        testServer.Server.Stop();
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
        await request;
    }

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            try { server.Dispose(); } catch { }
        }

        _http.Dispose();
    }

    private TestServer StartServer(Action<RemoteControlServer> configure, Func<PresentationStatus>? statusFactory = null)
    {
        var port = GetFreePort();
        var server = new RemoteControlServer(port)
        {
            GetStatus = statusFactory ?? (() => new PresentationStatus())
        };

        configure(server);
        server.Start();
        _servers.Add(server);

        return new TestServer(server, port);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record TestServer(RemoteControlServer Server, int Port) : IDisposable
    {
        public void Dispose()
        {
            Server.Dispose();
        }
    }
}
```

## 11) End-to-end runtime trace (how a session flows)
1. App starts MainWindow, initializes PreviewWebView + PreloadWebView.
2. Default or opened slide list populates `_slides` and triggers background caching.
3. Selecting a slide resolves URL (cached file for images when available) and navigates preview.
4. Press Play to open FullscreenWindow, which uses the same async URL resolver for every slide move.
5. Remote server endpoints call back into MainWindow actions, which update selection/fullscreen state and status.

```bash
grep -n "OnContentRendered\|LoadDefaultSlides\|SlideListBox_SelectionChanged\|Play_Click\|RemoteNext\|RemotePrev\|GetPresentationStatus" src/Present.NET/MainWindow.xaml.cs
```

```output
47:    protected override async void OnContentRendered(EventArgs e)
49:        base.OnContentRendered(e);
51:        LoadDefaultSlides();
76:    private void LoadDefaultSlides()
92:        _server.OnNext = () => Dispatcher.BeginInvoke(RemoteNext);
93:        _server.OnPrev = () => Dispatcher.BeginInvoke(RemotePrev);
99:        _server.GetStatus = GetPresentationStatus;
193:        if (e.Key == Key.F5) { Play_Click(this, new RoutedEventArgs()); e.Handled = true; }
266:    private void Play_Click(object sender, RoutedEventArgs e)
387:    private async void SlideListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
531:    private void RemoteNext()
542:    private void RemotePrev()
556:            Play_Click(this, new RoutedEventArgs());
572:    private PresentationStatus GetPresentationStatus()
```

## 12) Verification commands
The walkthrough reflects the code currently in this repository. These commands are the canonical quick checks for build and tests.

```bash
dotnet build Present.NET.sln -p:UseAppHost=false && dotnet test Present.NET.sln -c Release -p:UseAppHost=false
```

```output
  Determining projects to restore...
  All projects are up-to-date for restore.
  Present.NET.UiTests -> C:\Users\c.roper\Dev\Projects\present\tests\Present.NET.UiTests\bin\Debug\net8.0-windows\Present.NET.UiTests.dll
  Present.NET -> C:\Users\c.roper\Dev\Projects\present\src\Present.NET\bin\Debug\net8.0-windows\Present.NET.dll
  Present.NET.Tests -> C:\Users\c.roper\Dev\Projects\present\tests\Present.NET.Tests\bin\Debug\net8.0-windows\Present.NET.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.76
  Determining projects to restore...
  All projects are up-to-date for restore.
  Present.NET.UiTests -> C:\Users\c.roper\Dev\Projects\present\tests\Present.NET.UiTests\bin\Release\net8.0-windows\Present.NET.UiTests.dll
Test run for C:\Users\c.roper\Dev\Projects\present\tests\Present.NET.UiTests\bin\Release\net8.0-windows\Present.NET.UiTests.dll (.NETCoreApp,Version=v8.0)
  Present.NET -> C:\Users\c.roper\Dev\Projects\present\src\Present.NET\bin\Release\net8.0-windows\Present.NET.dll
VSTest version 17.11.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
  Present.NET.Tests -> C:\Users\c.roper\Dev\Projects\present\tests\Present.NET.Tests\bin\Release\net8.0-windows\Present.NET.Tests.dll
Test run for C:\Users\c.roper\Dev\Projects\present\tests\Present.NET.Tests\bin\Release\net8.0-windows\Present.NET.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 3 ms - Present.NET.UiTests.dll (net8.0)

Passed!  - Failed:     0, Passed:    48, Skipped:     0, Total:    48, Duration: 2 s - Present.NET.Tests.dll (net8.0)
```

## 13) Summary
The architecture is intentionally centralized around MainWindow orchestration: one slide collection, one preview WebView, one hidden warmup WebView, one cache service, and one embedded remote server. Fullscreen mode reuses the same URL resolution/caching logic, and the tests lock down the critical behavior around persistence, caching, and remote control.

