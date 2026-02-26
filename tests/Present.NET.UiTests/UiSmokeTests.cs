using FlaUI.Core;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using FlaApplication = FlaUI.Core.Application;

namespace Present.NET.UiTests;

public class UiSmokeTests
{
    [Fact]
    public void AppLaunches_MainWindowVisible_AndCloses()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PRESENT_UI_TESTS"), "1", StringComparison.Ordinal))
            return;

        var appPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Present.NET", "bin", "Debug", "net8.0-windows", "Present.NET.exe"));
        Assert.True(File.Exists(appPath), $"App executable not found at '{appPath}'. Build the app before running UI tests.");

        using var app = FlaApplication.Launch(appPath);
        using var automation = new UIA3Automation();

        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
        Assert.NotNull(window);
        Assert.Contains("Present.NET", window!.Title, StringComparison.OrdinalIgnoreCase);

        window.Close();
    }

    [Fact]
    public void RemoteCopy_CopiesUrlOnly_FromButtonAndDoubleClick()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PRESENT_UI_TESTS"), "1", StringComparison.Ordinal))
            return;

        var appPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Present.NET", "bin", "Debug", "net8.0-windows", "Present.NET.exe"));
        Assert.True(File.Exists(appPath), $"App executable not found at '{appPath}'. Build the app before running UI tests.");

        using var app = FlaApplication.Launch(appPath);
        using var automation = new UIA3Automation();

        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
        Assert.NotNull(window);

        var remoteInfo = Retry.WhileNull(
            () => window!.FindFirstDescendant(cf => cf.ByAutomationId("RemoteInfoText")),
            timeout: TimeSpan.FromSeconds(10)).Result;

        Assert.NotNull(remoteInfo);

        var expectedUrl = Retry.WhileNull(
            () => ExtractUrl(remoteInfo!.Name),
            timeout: TimeSpan.FromSeconds(10)).Result;

        Assert.False(string.IsNullOrWhiteSpace(expectedUrl));

        SetClipboardTextSta("sentinel");

        var copyButton = window!.FindFirstDescendant(cf => cf.ByAutomationId("CopyRemoteButton"));
        Assert.NotNull(copyButton);
        Mouse.Click(copyButton!.GetClickablePoint());

        var copiedFromButton = Retry.WhileNull(
            () => GetClipboardTextSta() == "sentinel" ? null : GetClipboardTextSta(),
            timeout: TimeSpan.FromSeconds(5)).Result;

        Assert.Equal(expectedUrl, copiedFromButton);
        Assert.DoesNotContain("Remote:", copiedFromButton!, StringComparison.OrdinalIgnoreCase);

        SetClipboardTextSta("sentinel2");

        var remoteContainer = window.FindFirstDescendant(cf => cf.ByAutomationId("RemoteInfoContainer"))
            ?? remoteInfo;

        var clickablePoint = remoteContainer!.GetClickablePoint();
        Mouse.DoubleClick(clickablePoint);

        var copiedFromDoubleClick = Retry.WhileNull(
            () => GetClipboardTextSta() == "sentinel2" ? null : GetClipboardTextSta(),
            timeout: TimeSpan.FromSeconds(5)).Result;

        Assert.Equal(expectedUrl, copiedFromDoubleClick);
        Assert.DoesNotContain("Remote:", copiedFromDoubleClick!, StringComparison.OrdinalIgnoreCase);

        window.Close();
    }

    private static string? ExtractUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = Regex.Match(text, @"https?://\S+");
        return match.Success ? match.Value : null;
    }

    private static void SetClipboardTextSta(string text)
    {
        RunSta(() => Clipboard.SetText(text));
    }

    private static string? GetClipboardTextSta()
    {
        string? value = null;
        RunSta(() =>
        {
            value = Clipboard.ContainsText()
                ? Clipboard.GetText()
                : null;
        });
        return value;
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
            throw new InvalidOperationException("STA operation failed", error);
    }
}
