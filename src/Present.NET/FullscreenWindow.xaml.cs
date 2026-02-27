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
        try
        {
            await SlideWebView.EnsureCoreWebView2Async();
            _webViewReady = true;
            SlideWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            SlideWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 runtime is not installed.\n\n{ex.Message}\n\n" +
                "Download from: https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                "WebView2 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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
