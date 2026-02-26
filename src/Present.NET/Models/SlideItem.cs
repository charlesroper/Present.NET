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
