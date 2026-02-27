using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Present.NET.Models;
using Present.NET.Services;
using System.Runtime.InteropServices;

namespace Present.NET;

public partial class MainWindow : Window
{
    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------
    private readonly ObservableCollection<SlideItem> _slides = new();
    private string? _currentFilePath;
    private bool _isDirty;
    private double _zoomFactor = 1.0;
    private bool _previewWebViewReady;
    private string? _remoteControlUrl;
    private readonly SlideCacheService _slideCacheService = new();
    private readonly SemaphoreSlim _cacheOperationLock = new(1, 1);
    private CancellationTokenSource? _cacheCts;
    private ThemePreference _themePreference;
    private bool _effectiveDarkTheme;

    private RemoteControlServer? _server;
    private FullscreenWindow? _fullscreenWindow;

    // Drag-reorder state
    private Point _dragStart;
    private bool _isDragging;

    // -----------------------------------------------------------------------
    // Construction / Startup
    // -----------------------------------------------------------------------
    public MainWindow()
    {
        InitializeComponent();
        InitializeTheme();
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyWindowChromeTheme(_effectiveDarkTheme);
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
            return;
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
        if (_isDirty && _slides.Count > 0)
        {
            var result = MessageBox.Show(
                "Save changes before closing?", "Present.NET",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (result == MessageBoxResult.Yes) SaveCurrentFile();
        }

        PersistenceService.SaveDefault(_slides.Select(s => s.Url));
        _cacheCts?.Cancel();
        _server?.Stop();
        _server?.Dispose();
        _fullscreenWindow?.Close();
    }

    // -----------------------------------------------------------------------
    // Keyboard shortcuts
    // -----------------------------------------------------------------------
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F5) { Play_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.O && e.KeyboardDevice.Modifiers == ModifierKeys.Control) { OpenFile_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.S && e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { SaveFileAs_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.S && e.KeyboardDevice.Modifiers == ModifierKeys.Control) { SaveFile_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if ((e.Key == Key.OemPlus || e.Key == Key.Add) && e.KeyboardDevice.Modifiers == ModifierKeys.Control) { ZoomIn(); e.Handled = true; }
        else if ((e.Key == Key.OemMinus || e.Key == Key.Subtract) && e.KeyboardDevice.Modifiers == ModifierKeys.Control) { ZoomOut(); e.Handled = true; }
        else if ((e.Key == Key.D0 || e.Key == Key.NumPad0) && e.KeyboardDevice.Modifiers == ModifierKeys.Control) { ZoomReset(); e.Handled = true; }
    }

    // -----------------------------------------------------------------------
    // File menu
    // -----------------------------------------------------------------------
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

    private void UseSystemTheme_Click(object sender, RoutedEventArgs e) => SetThemePreference(ThemePreference.System);
    private void UseLightTheme_Click(object sender, RoutedEventArgs e) => SetThemePreference(ThemePreference.Light);
    private void UseDarkTheme_Click(object sender, RoutedEventArgs e) => SetThemePreference(ThemePreference.Dark);

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
            if (sender is TextBox tb)
            {
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                SlideListBox.Focus();
            }
            e.Handled = true;
        }
    }

    // -----------------------------------------------------------------------
    // Drag-to-reorder
    // -----------------------------------------------------------------------
    private void SlideListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _isDragging = false;
    }

    private void SlideListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_isDragging) return;

        var pos = e.GetPosition(null);
        var diff = _dragStart - pos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        // Check if source is a ListBoxItem (not a TextBox)
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<TextBox>(source) != null) return;

        var item = FindAncestor<ListBoxItem>(source);
        if (item?.DataContext is SlideItem slide)
        {
            _isDragging = true;
            DragDrop.DoDragDrop(item, slide, DragDropEffects.Move);
            _isDragging = false;
        }
    }

    private void SlideListBox_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(SlideItem))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void SlideListBox_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(SlideItem))) return;
        var dragged = (SlideItem)e.Data.GetData(typeof(SlideItem));

        // Find the target item
        var target = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (target?.DataContext is SlideItem targetSlide && !ReferenceEquals(dragged, targetSlide))
        {
            var oldIdx = _slides.IndexOf(dragged);
            var newIdx = _slides.IndexOf(targetSlide);
            _slides.Move(oldIdx, newIdx);
            SlideListBox.SelectedItem = dragged;
            MarkDirty();
        }
        e.Handled = true;
    }

    // -----------------------------------------------------------------------
    // Remote control actions
    // -----------------------------------------------------------------------
    private void RemoteNext()
    {
        if (_fullscreenWindow != null)
            _fullscreenWindow.NavigateNext();
        else if (_slides.Count > 0)
        {
            var idx = (SlideListBox.SelectedIndex + 1) % _slides.Count;
            SlideListBox.SelectedIndex = idx;
        }
    }

    private void RemotePrev()
    {
        if (_fullscreenWindow != null)
            _fullscreenWindow.NavigatePrev();
        else if (_slides.Count > 0)
        {
            var idx = (SlideListBox.SelectedIndex - 1 + _slides.Count) % _slides.Count;
            SlideListBox.SelectedIndex = idx;
        }
    }

    private void RemotePlay()
    {
        if (_fullscreenWindow == null)
            Play_Click(this, new RoutedEventArgs());
    }

    private void RemoteStop()
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
            {
                slide.CacheState = SlideCacheState.Unknown;
                slide.Source = SlideSource.Unknown;
            }

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
            slide.Source = SlideSource.Unknown;
            return;
        }

        if (!SlideHelper.IsImageUrl(slide.Url))
        {
            slide.CacheState = SlideCacheState.Unknown;
            slide.Source = SlideSource.Network;
            return;
        }

        try
        {
            slide.CacheState = SlideCacheState.Caching;
            slide.Source = SlideSource.Unknown;

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

    private void UpdateSlideCountLabel()
    {
        SlideCountLabel.Text = _slides.Count == 1 ? "(1 slide)" : $"({_slides.Count} slides)";
    }

    private void InitializeTheme()
    {
        _themePreference = PersistenceService.LoadThemePreference();
        ApplyTheme(_themePreference);
    }

    private void SetThemePreference(ThemePreference preference)
    {
        _themePreference = preference;
        PersistenceService.SaveThemePreference(preference);
        ApplyTheme(preference);
    }

    private void ApplyTheme(ThemePreference preference)
    {
        var effectiveTheme = preference switch
        {
            ThemePreference.System => IsSystemDarkModeEnabled() ? ThemePreference.Dark : ThemePreference.Light,
            _ => preference
        };

        _effectiveDarkTheme = effectiveTheme == ThemePreference.Dark;
        ApplyThemeBrushes(_effectiveDarkTheme);
        ApplyWindowChromeTheme(_effectiveDarkTheme);
        SyncThemeMenuState(preference);
    }

    private static bool IsSystemDarkModeEnabled()
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var appsUseLightTheme = personalizeKey?.GetValue("AppsUseLightTheme");
            return appsUseLightTheme is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyThemeBrushes(bool dark)
    {
        if (Application.Current?.Resources is not ResourceDictionary resources)
            return;

        SetBrush(resources, "AppWindowBackgroundBrush", dark ? "#1E1E1E" : "#F5F5F5");
        SetBrush(resources, "MenuBackgroundBrush", dark ? "#252526" : "#FFFFFF");
        SetBrush(resources, "MenuPopupBackgroundBrush", dark ? "#2B2B31" : "#FFFFFF");
        SetBrush(resources, "MenuPopupBorderBrush", dark ? "#4B4B52" : "#D0D0D0");
        SetBrush(resources, "MenuItemHoverBackgroundBrush", dark ? "#3A3D41" : "#E5F3FB");
        SetBrush(resources, "MenuItemHoverBorderBrush", dark ? "#50545A" : "#99D1F5");
        SetBrush(resources, "ToolbarBackgroundBrush", dark ? "#2D2D30" : "#F3F3F3");
        SetBrush(resources, "ToolbarForegroundBrush", dark ? "#F0F0F0" : "#1E1E1E");
        SetBrush(resources, "ToolbarSeparatorBrush", dark ? "#4B4B50" : "#BEBEBE");
        SetBrush(resources, "SidebarBackgroundBrush", dark ? "#252526" : "#FAFAFA");
        SetBrush(resources, "SidebarHeaderBackgroundBrush", dark ? "#333337" : "#E8E8E8");
        SetBrush(resources, "SidebarHeaderBorderBrush", dark ? "#3F3F46" : "#D0D0D0");
        SetBrush(resources, "PanelBorderBrush", dark ? "#3F3F46" : "#E8E8E8");
        SetBrush(resources, "PrimaryTextBrush", dark ? "#F0F0F0" : "#222222");
        SetBrush(resources, "SecondaryTextBrush", dark ? "#C8C8C8" : "#555555");
        SetBrush(resources, "MutedTextBrush", dark ? "#A0A0A0" : "#888888");
        SetBrush(resources, "TextInputForegroundBrush", dark ? "#F0F0F0" : "#333333");
        SetBrush(resources, "ItemHoverBackgroundBrush", dark ? "#3A3D41" : "#E5F3FB");
        SetBrush(resources, "ItemSelectedBackgroundBrush", "#0078D4");
        SetBrush(resources, "ItemSelectedHoverBackgroundBrush", "#0067BE");
        SetBrush(resources, "SplitterBrush", dark ? "#454545" : "#D0D0D0");
        SetBrush(resources, "ButtonBackgroundBrush", dark ? "#3A3A3D" : "#FFFFFF");
        SetBrush(resources, "ButtonBorderBrush", dark ? "#5A5A5E" : "#C8C8C8");
        SetBrush(resources, "ButtonHoverBackgroundBrush", dark ? "#4A4A4E" : "#EDEDED");
        SetBrush(resources, "ButtonHoverBorderBrush", dark ? "#868690" : "#9A9A9A");
        SetBrush(resources, "ButtonPressedBackgroundBrush", dark ? "#56565A" : "#DDDDDD");
        SetBrush(resources, "ButtonPressedBorderBrush", dark ? "#A0A0AE" : "#7E7E7E");
        SetBrush(resources, "PreviewPaneBackgroundBrush", dark ? "#1F1F22" : "#222222");
        SetBrush(resources, "PreviewPlaceholderTitleBrush", dark ? "#8C8C95" : "#5E5E5E");
        SetBrush(resources, "PreviewPlaceholderBodyBrush", dark ? "#A8A8B2" : "#777777");
        SetBrush(resources, "ScrollBarTrackBrush", dark ? "#2E2E34" : "#E7E7E7");
        SetBrush(resources, "ScrollBarThumbBrush", dark ? "#5A5A63" : "#B0B0B0");
        SetBrush(resources, "ScrollBarThumbHoverBrush", dark ? "#70707C" : "#8F8F8F");
        SetBrush(resources, "ScrollBarArrowBrush", dark ? "#D0D0D8" : "#4F4F4F");
        SetBrush(resources, "TooltipBackgroundBrush", dark ? "#2D2D30" : "#FFFFFF");
        SetBrush(resources, "TooltipForegroundBrush", dark ? "#F0F0F0" : "#1E1E1E");
        SetBrush(resources, "TooltipBorderBrush", dark ? "#5A5A5E" : "#777777");

        resources[SystemColors.MenuBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#2B2B31" : "#FFFFFF"));
        resources[SystemColors.MenuTextBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#F0F0F0" : "#222222"));
        resources[SystemColors.HighlightBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#3A3D41" : "#E5F3FB"));
        resources[SystemColors.HighlightTextBrushKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#FFFFFF" : "#111111"));
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hexColor)
    {
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
    }

    private void SyncThemeMenuState(ThemePreference preference)
    {
        UseSystemThemeMenuItem.IsChecked = preference == ThemePreference.System;
        UseLightThemeMenuItem.IsChecked = preference == ThemePreference.Light;
        UseDarkThemeMenuItem.IsChecked = preference == ThemePreference.Dark;
    }

    private void ApplyWindowChromeTheme(bool dark)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        if (Environment.OSVersion.Version.Major < 10)
            return;

        var useDark = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref useDark, Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

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
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
