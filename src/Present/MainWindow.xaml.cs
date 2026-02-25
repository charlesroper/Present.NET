using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Present.Models;
using Present.Services;

namespace Present;

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
        SlideListBox.ItemsSource = _slides;
        _slides.CollectionChanged += (_, _) => RefreshNumbers();
    }

    private async void Window_Loaded_unused() { }

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
            _previewWebViewReady = true;
            PreviewWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            PreviewWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
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
    }

    private void StartRemoteServer()
    {
        _server = new RemoteControlServer(9123);
        _server.OnNext = () => Dispatcher.Invoke(RemoteNext);
        _server.OnPrev = () => Dispatcher.Invoke(RemotePrev);
        _server.OnPlay = () => Dispatcher.Invoke(RemotePlay);
        _server.OnStop = () => Dispatcher.Invoke(RemoteStop);
        _server.OnZoomIn = () => Dispatcher.Invoke(ZoomIn);
        _server.OnZoomOut = () => Dispatcher.Invoke(ZoomOut);
        _server.OnScroll = dy => Dispatcher.Invoke(() => RemoteScroll(dy));
        _server.GetStatus = GetPresentationStatus;

        try
        {
            _server.Start();
            var ip = GetLocalIpAddress();
            RemoteInfoText.Text = $"Remote: http://{ip}:9123/";
        }
        catch (Exception ex)
        {
            RemoteInfoText.Text = "Remote control unavailable";
            System.Diagnostics.Debug.WriteLine($"Remote server error: {ex.Message}");
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
                "Save changes before closing?", "Present",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (result == MessageBoxResult.Yes) SaveCurrentFile();
        }

        PersistenceService.SaveDefault(_slides.Select(s => s.Url));
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
            var r = MessageBox.Show("Save current file first?", "Present",
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
            MessageBox.Show("No slides to present. Add some URLs first.", "Present",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var startIndex = SlideListBox.SelectedIndex >= 0 ? SlideListBox.SelectedIndex : 0;
        _fullscreenWindow = new FullscreenWindow(_slides.ToList(), startIndex, _zoomFactor);
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
    // Slide list selection → preview
    // -----------------------------------------------------------------------
    private void SlideListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SlideListBox.SelectedItem is SlideItem selected)
        {
            NavigatePreview(selected.Url);
        }
        else
        {
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewWebView.Visibility = Visibility.Collapsed;
        }
    }

    private void NavigatePreview(string url)
    {
        if (!_previewWebViewReady || string.IsNullOrWhiteSpace(url)) return;
        if (url == "https://") return; // placeholder URL, don't navigate

        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        PreviewWebView.Visibility = Visibility.Visible;

        if (SlideHelper.IsImageUrl(url))
            PreviewWebView.NavigateToString(SlideHelper.GetImageHtml(url));
        else
            PreviewWebView.CoreWebView2.Navigate(url);

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
                MarkDirty();
                // Refresh preview if this is the selected item
                if (SlideListBox.SelectedItem == item)
                    NavigatePreview(item.Url);
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
        var dirty = _isDirty ? "• " : "";
        var file = _currentFilePath != null
            ? System.IO.Path.GetFileName(_currentFilePath)
            : "Untitled";
        Title = $"{dirty}{file} – Present";
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
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
