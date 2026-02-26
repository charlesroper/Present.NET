using Present.NET.Models;

namespace Present.NET.Tests;

public class SlideItemTests
{
    [Fact]
    public void Url_RaisesPropertyChanged_WhenValueChanges()
    {
        var slide = new SlideItem("https://example.com", 1);
        var raised = new List<string>();
        slide.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
                raised.Add(e.PropertyName);
        };

        slide.Url = "https://example.org";

        Assert.Contains(nameof(SlideItem.Url), raised);
    }

    [Fact]
    public void Number_DoesNotRaisePropertyChanged_WhenValueIsUnchanged()
    {
        var slide = new SlideItem("https://example.com", 1);
        var raised = new List<string>();
        slide.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
                raised.Add(e.PropertyName);
        };

        slide.Number = 1;

        Assert.DoesNotContain(nameof(SlideItem.Number), raised);
    }

    [Fact]
    public void Number_RaisesPropertyChanged_WhenValueChanges()
    {
        var slide = new SlideItem("https://example.com", 1);
        var raised = new List<string>();
        slide.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
                raised.Add(e.PropertyName);
        };

        slide.Number = 2;

        Assert.Contains(nameof(SlideItem.Number), raised);
    }

    [Fact]
    public void Url_CoercesNullToEmptyString()
    {
        var slide = new SlideItem("https://example.com", 1);

        slide.Url = null!;

        Assert.Equal(string.Empty, slide.Url);
    }

    [Fact]
    public void CacheState_UpdatesCacheLabel()
    {
        var slide = new SlideItem("https://example.com", 1);

        slide.CacheState = SlideCacheState.Caching;
        Assert.Equal("Caching", slide.CacheLabel);

        slide.CacheState = SlideCacheState.Cached;
        Assert.Equal("Cached", slide.CacheLabel);
    }

    [Fact]
    public void Source_UpdatesSourceLabel()
    {
        var slide = new SlideItem("https://example.com", 1);

        slide.Source = SlideSource.Network;
        Assert.Equal("network", slide.SourceLabel);

        slide.Source = SlideSource.Cache;
        Assert.Equal("cache", slide.SourceLabel);
    }

    [Fact]
    public void CacheSummary_ShowsBothCacheAndSource()
    {
        var slide = new SlideItem("https://example.com", 1)
        {
            CacheState = SlideCacheState.Cached,
            Source = SlideSource.Cache
        };

        Assert.Equal("Cached (cache)", slide.CacheSummary);
    }
}
