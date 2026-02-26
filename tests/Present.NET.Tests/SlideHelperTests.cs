using Present.NET.Models;

namespace Present.NET.Tests;

public class SlideHelperTests
{
    [Theory]
    [InlineData("https://example.com/slide.png")]
    [InlineData("https://example.com/slide.gif")]
    [InlineData("https://example.com/slide.jpeg")]
    [InlineData("https://example.com/slide.JPG")]
    [InlineData("https://example.com/path/slide.webp?version=1")]
    [InlineData("https://example.com/path/slide.svg#section")]
    public void IsImageUrl_ReturnsTrue_ForSupportedImageUrls(string url)
    {
        Assert.True(SlideHelper.IsImageUrl(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/page.html")]
    [InlineData("not-a-url")]
    public void IsImageUrl_ReturnsFalse_ForNonImageUrls(string url)
    {
        Assert.False(SlideHelper.IsImageUrl(url));
    }

    [Fact]
    public void GetImageHtml_EncodesHtmlSensitiveCharacters()
    {
        var html = SlideHelper.GetImageHtml("https://example.com/img.jpg?a=1&b=<bad>&q=\"x\"");

        Assert.Contains("a=1&amp;b=&lt;bad&gt;", html);
        Assert.Contains("&quot;x&quot;", html);
    }
}
