using System.ComponentModel;

namespace Present.Models;

/// <summary>
/// Represents a single slide entry (a URL) in the presentation.
/// </summary>
public class SlideItem : INotifyPropertyChanged
{
    private string _url = string.Empty;
    private int _number;

    public string Url
    {
        get => _url;
        set
        {
            if (_url != value)
            {
                _url = value;
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

    public SlideItem(string url, int number)
    {
        _url = url;
        _number = number;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
