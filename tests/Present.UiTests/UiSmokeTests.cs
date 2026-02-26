using FlaUI.Core;
using FlaUI.UIA3;

namespace Present.UiTests;

public class UiSmokeTests
{
    [Fact]
    public void AppLaunches_MainWindowVisible_AndCloses()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PRESENT_UI_TESTS"), "1", StringComparison.Ordinal))
            return;

        var appPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Present", "bin", "Debug", "net8.0-windows", "Present.exe"));
        Assert.True(File.Exists(appPath), $"App executable not found at '{appPath}'. Build the app before running UI tests.");

        using var app = Application.Launch(appPath);
        using var automation = new UIA3Automation();

        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
        Assert.NotNull(window);
        Assert.Contains("Present", window!.Title, StringComparison.OrdinalIgnoreCase);

        window.Close();
    }
}
