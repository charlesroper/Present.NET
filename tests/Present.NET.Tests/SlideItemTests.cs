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
        Assert.Equal("Live", slide.SourceLabel);

        slide.Source = SlideSource.Cache;
        Assert.Equal("Cached", slide.SourceLabel);
    }

    [Fact]
    public void CacheSummary_ShowsCachedForLocalSlides()
    {
        var slide = new SlideItem("https://example.com", 1)
        {
            CacheState = SlideCacheState.Cached,
            Source = SlideSource.Cache
        };

        Assert.Equal("Cached", slide.CacheSummary);
    }

    [Fact]
    public void CacheSummary_ShowsLiveForNetworkSlides()
    {
        var slide = new SlideItem("https://example.com", 1)
        {
            CacheState = SlideCacheState.Cached,
            Source = SlideSource.Network
        };

        Assert.Equal("Live", slide.CacheSummary);
    }

    [Fact]
    public void CacheStatusTooltip_ExplainsCachedState()
    {
        var slide = new SlideItem("https://example.com", 1)
        {
            CacheState = SlideCacheState.Cached,
            Source = SlideSource.Cache
        };

        Assert.Equal("This slide is saved on your computer. It should open quickly during your presentation, even if your internet connection is slow.", slide.CacheStatusTooltip);
    }

    [Fact]
    public void CacheStatusTooltip_ExplainsLiveState()
    {
        var slide = new SlideItem("https://example.com", 1)
        {
            CacheState = SlideCacheState.Cached,
            Source = SlideSource.Network
        };

        Assert.Equal("This slide is a live web page. In Present.NET, web pages are always live and load from the internet each time you open them, so a stable connection is important.", slide.CacheStatusTooltip);
    }

    [Fact]
    public void CacheStatusTooltip_ExplainsFailedState()
    {
        var slide = new SlideItem("https://example.com", 1)
        {
            CacheState = SlideCacheState.Failed,
            Source = SlideSource.Failed
        };

        Assert.Equal("This slide could not be prepared. Check the slide address and your internet connection, then try again.", slide.CacheStatusTooltip);
    }

    [Fact]
    public void CacheStatusTooltip_ExplainsCachingState()
    {
        var slide = new SlideItem("https://example.com", 1)
        {
            CacheState = SlideCacheState.Caching
        };

        Assert.Equal("This slide is being prepared right now. It will be ready in a moment.", slide.CacheStatusTooltip);
    }
}
