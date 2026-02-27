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
                OnPropertyChanged(nameof(CacheStatusTooltip));
                OnPropertyChanged(nameof(IsCaching));
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

    public bool IsCaching => CacheState == SlideCacheState.Caching;

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
                OnPropertyChanged(nameof(CacheStatusTooltip));
            }
        }
    }

    public string SourceLabel => Source switch
    {
        SlideSource.Cache => "Cached",
        SlideSource.Network => "Live",
        SlideSource.Failed => "Failed",
        _ => ""
    };

    public string CacheSummary
    {
        get
        {
            if (CacheState == SlideCacheState.Failed || Source == SlideSource.Failed)
                return "Failed";

            if (CacheState == SlideCacheState.Caching)
                return "Caching";

            if (Source == SlideSource.Network)
                return "Live";

            if (Source == SlideSource.Cache || CacheState == SlideCacheState.Cached)
                return "Cached";

            return "";
        }
    }

    public string CacheStatusTooltip => CacheSummary switch
    {
        "Cached" => "This slide is saved on your computer. It should open quickly during your presentation, even if your internet connection is slow.",
        "Live" => "This slide is a live web page. In Present.NET, web pages are always live and load from the internet each time you open them, so a stable connection is important.",
        "Failed" => "This slide could not be prepared. Check the slide address and your internet connection, then try again.",
        "Caching" => "This slide is being prepared right now. It will be ready in a moment.",
        _ => ""
    };

    public SlideItem(string url, int number)
    {
        _url = url ?? string.Empty;
        _number = number;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
