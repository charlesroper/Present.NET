using Present.Models;

namespace Present.Tests;

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
    public void Url_CoercesNullToEmptyString()
    {
        var slide = new SlideItem("https://example.com", 1);

        slide.Url = null!;

        Assert.Equal(string.Empty, slide.Url);
    }
}
